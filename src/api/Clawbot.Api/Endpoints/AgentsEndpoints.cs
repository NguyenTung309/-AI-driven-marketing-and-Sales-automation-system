using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Api.Auth;
using Clawbot.Api.Jobs;
using Clawbot.Api.Middleware;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// M25 — agent control & observability over the existing AgentConfig (`agents` table).
public sealed record AgentSettingsResponse(
    string Code,
    string DisplayName,
    string AgentType,
    string Model,
    string Status,
    string Provider,
    string SystemPrompt,
    double Temperature,
    int MaxTokens,
    IReadOnlyList<string> SkillFiles,
    IReadOnlyList<string> KbModules,
    // Tool grants the orchestrator's ReAct worker honours. Stored on the matching agent_definitions row,
    // not the agents table — empty means the agent runs text-only (no system actions).
    IReadOnlyList<string> AllowedTools,
    Guid? LlmConfigId,
    DateTimeOffset UpdatedAt);

public sealed record AgentSettingsRequest(
    string? DisplayName,
    string? Model,
    string? Provider,
    string? SystemPrompt,
    double? Temperature,
    int? MaxTokens,
    IReadOnlyList<string>? SkillFiles,
    IReadOnlyList<string>? KbModules,
    // null = leave unchanged; otherwise replace the agent_definitions tool grants with this list.
    IReadOnlyList<string>? AllowedTools = null,
    // Tri-state: null = leave unchanged, Guid.Empty = unbind, otherwise bind to that config.
    Guid? LlmConfigId = null);

public sealed record AgentToolInfo(string Name, string Description, string Risk, string Permission);

public sealed record AgentSandboxRequest(string Message);
public sealed record AgentSandboxResponse(Guid SessionId, string Reply, DateTimeOffset SentAt);

public static class AgentsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAgents(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/agents").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync).RequirePermission("agent.read");
        grp.MapGet("/tools", ToolsCatalogAsync).RequirePermission("agent.read");
        grp.MapPost("/{code}/enable", EnableAsync).RequirePermission("agent.manage");
        grp.MapPost("/{code}/disable", DisableAsync).RequirePermission("agent.manage");
        grp.MapGet("/{code}/settings", SettingsAsync).RequirePermission("agent.read");
        grp.MapPut("/{code}/settings", UpdateSettingsAsync).RequirePermission("agent.manage");
        grp.MapPost("/{code}/sandbox", SandboxAsync).RequirePermission("agent.manage");
        grp.MapGet("/{code}/traces", TracesAsync).RequirePermission("agent.read");

        return grp;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct = default)
    {
        _ = tenants.Require(); // AgentConfigs are tenant query-filtered automatically
        var agents = await db.AgentConfigs
            .Where(a => a.DeletedAt == null)
            .OrderBy(a => a.Code)
            .Select(a => new
            {
                a.Code,
                DisplayName = DisplayAgentLabel(a.Code, a.DisplayName, a.AgentType),
                a.AgentType,
                a.Model,
                a.Status,
                a.UpdatedAt,
                a.LlmConfigId,
                LastRunAt = db.AgentSessions.Where(s => s.AgentId == a.Id).Max(s => (DateTimeOffset?)s.StartedAt),
            })
            .ToListAsync(ct);

        return Results.Ok(new { items = agents });
    }

    private static async Task<IResult> SetStatusAsync(
        string code, bool enable, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var agent = await db.AgentConfigs.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (agent is null) return Results.NotFound();

        if (enable) agent.Start();
        else agent.Stop();
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { agent.Code, agent.Status });
    }

    // Static tool catalog (name/description/risk/permission) so the UI can offer a checklist of grantable tools.
    private static IResult ToolsCatalogAsync() =>
        Results.Ok(new
        {
            items = ToolRegistryFactory.KnownTools
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new AgentToolInfo(kv.Key, kv.Value.Description, kv.Value.Risk.ToString(), kv.Value.Permission))
                .ToArray(),
        });

    private static async Task<IResult> SettingsAsync(string code, AppDbContext db, ITenantAccessor tenants, CancellationToken ct = default)
    {
        _ = tenants.Require();
        var agent = await db.AgentConfigs.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (agent is null) return Results.NotFound();
        var allowedTools = await LoadAllowedToolsAsync(db, code, ct);
        return Results.Ok(ToSettings(agent, allowedTools));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        string code,
        AgentSettingsRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        IPermissionResolver permissions,
        HttpContext http,
        CancellationToken ct = default)
    {
        if (req.LlmConfigId is not null && !await HasPermissionAsync(http, permissions, "llm-configs:manage", ct))
            return Forbidden(http);

        var tenantId = tenants.Require().TenantId;
        var agent = await db.AgentConfigs.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (agent is null) return Results.NotFound();

        // Validate any requested tool grants against the known catalog before touching anything (fail fast).
        if (req.AllowedTools is not null)
        {
            var unknown = req.AllowedTools
                .Select(t => t.Trim())
                .Where(t => t.Length > 0 && !ToolRegistryFactory.KnownTools.ContainsKey(t))
                .ToArray();
            if (unknown.Length > 0)
                return Results.BadRequest(new { error = "unknown_tools", tools = unknown });
        }

        var displayName = NormalizeText(req.DisplayName, agent.DisplayName, maxLength: 256);
        var model = NormalizeText(req.Model, agent.Model, maxLength: 128);
        if (string.IsNullOrWhiteSpace(displayName)) return Results.BadRequest(new { error = "display_name_required" });
        if (string.IsNullOrWhiteSpace(model)) return Results.BadRequest(new { error = "model_required" });

        // Per-agent provider binding (tri-state) + D9 cross-provider model guard. Resolve the binding
        // the request *leaves the agent in*, then validate the effective model against that provider —
        // not only on rebind, so a later model-only edit can't drift a Claude string onto an OpenAI bind.
        var targetConfigId = req.LlmConfigId switch
        {
            null => agent.LlmConfigId,                  // unchanged
            { } g when g == Guid.Empty => (Guid?)null,  // unbind
            { } g => g,                                  // bind to g
        };

        // Only re-validate when the model or the binding was actually touched, so unrelated edits
        // (e.g. display name) don't trip on pre-existing mismatches.
        if ((req.Model is not null || req.LlmConfigId is not null) && targetConfigId is { } cfgId)
        {
            var boundProvider = await db.LlmConfigs
                .Where(c => c.Id == cfgId && c.TenantId == tenantId)
                .Select(c => c.Provider)
                .FirstOrDefaultAsync(ct);
            if (boundProvider is null) return Results.BadRequest(new { error = "invalid_llm_config" });
            if (!IsModelCompatibleWithProvider(boundProvider, model))
                return Results.BadRequest(new { error = "model_provider_mismatch" });
        }

        if (req.LlmConfigId is { } bindId)
            agent.BindLlmConfig(bindId == Guid.Empty ? null : bindId, clock.UtcNow);

        var config = ReadRuntimeConfig(agent.ConfigJson);
        config.Provider = NormalizeText(req.Provider, config.Provider, maxLength: 64);
        config.SystemPrompt = (req.SystemPrompt ?? config.SystemPrompt).Trim();
        config.Temperature = req.Temperature.HasValue ? Math.Clamp(req.Temperature.Value, 0, 2) : config.Temperature;
        config.MaxTokens = req.MaxTokens.HasValue ? Math.Clamp(req.MaxTokens.Value, 128, 32000) : config.MaxTokens;

        agent.UpdateSettings(
            displayName,
            model,
            SerializeList(req.SkillFiles, agent.SkillFilesJson),
            SerializeList(req.KbModules, agent.KbModulesJson),
            JsonSerializer.Serialize(config, JsonOptions),
            clock.UtcNow);

        // Tool grants live on the agent_definitions row the orchestrator plans over, not the agents table.
        if (req.AllowedTools is not null)
        {
            var definition = await db.AgentDefinitions.FirstOrDefaultAsync(d => d.Code == code, ct);
            if (definition is not null)
            {
                var cleaned = req.AllowedTools
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                definition.SetAllowedTools(JsonSerializer.Serialize(cleaned, JsonOptions), clock.UtcNow);
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToSettings(agent, await LoadAllowedToolsAsync(db, code, ct)));
    }

    // Chạy thử agent = 1 lượt LLM thật -> đưa vào job (thấy được, huỷ được). Không bắn thông báo
    // lúc xong: user đang ngồi trong sandbox chờ câu trả lời (NotifyOnSuccess=false ở handler).
    private static async Task<IResult> SandboxAsync(
        string code,
        AgentSandboxRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct = default)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest(new { error = "message_required" });

        var agent = await db.AgentConfigs.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (agent is null) return Results.NotFound();

        var config = ReadRuntimeConfig(agent.ConfigJson);
        var jobId = await jobs.LaunchAsync(
            AgentSandboxJobHandler.JobType,
            $"Chạy thử agent {agent.DisplayName}",
            new AgentSandboxJobPayload(agent.Id, agent.Code, config.SystemPrompt, req.Message.Trim()),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;

    private static Task<IResult> EnableAsync(string code, AppDbContext db, ITenantAccessor tenants, CancellationToken ct = default)
        => SetStatusAsync(code, true, db, tenants, ct);

    private static Task<IResult> DisableAsync(string code, AppDbContext db, ITenantAccessor tenants, CancellationToken ct = default)
        => SetStatusAsync(code, false, db, tenants, ct);

    private static async Task<IResult> TracesAsync(
        string code,
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var agent = await db.AgentConfigs.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (agent is null) return Results.NotFound();

        // Sessions are tenant query-filtered, so scoping traces via their session ids is tenant-safe.
        var sessionIds = db.AgentSessions.Where(s => s.AgentId == agent.Id).Select(s => s.Id);
        var query = db.AgentTraces.Where(t => sessionIds.Contains(t.SessionId));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.Id, t.SessionId, t.AgentName, t.Phase, t.Message, t.OccurredAt })
            .ToListAsync(ct);

        return Results.Ok(new { total, page, pageSize, items });
    }

    private static async Task<bool> HasPermissionAsync(
        HttpContext http,
        IPermissionResolver permissions,
        string code,
        CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirst("role_id")?.Value, out var roleId) || roleId == Guid.Empty)
            return false;

        var granted = await permissions.GetPermissionsAsync(roleId, ct);
        return granted.Contains(code);
    }

    private static IResult Forbidden(HttpContext http) =>
        Results.Json(
            new { errorCode = "forbidden", message = "Không có quyền", requestId = http.TraceIdentifier },
            statusCode: StatusCodes.Status403Forbidden);

    // Reads the tool grants from the matching agent_definitions row (separate aggregate from the agents table).
    private static async Task<IReadOnlyList<string>> LoadAllowedToolsAsync(AppDbContext db, string code, CancellationToken ct)
    {
        var json = await db.AgentDefinitions
            .Where(d => d.Code == code)
            .Select(d => d.AllowedToolsJson)
            .FirstOrDefaultAsync(ct);
        return DeserializeList(json);
    }

    private static AgentSettingsResponse ToSettings(AgentConfig agent, IReadOnlyList<string> allowedTools)
    {
        var config = ReadRuntimeConfig(agent.ConfigJson);
        return new AgentSettingsResponse(
            agent.Code,
            DisplayAgentLabel(agent.Code, agent.DisplayName, agent.AgentType),
            agent.AgentType,
            agent.Model,
            agent.Status,
            config.Provider,
            config.SystemPrompt,
            config.Temperature,
            config.MaxTokens,
            DeserializeList(agent.SkillFilesJson),
            DeserializeList(agent.KbModulesJson),
            allowedTools,
            agent.LlmConfigId,
            agent.UpdatedAt);
    }

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    // D9 — loose cross-provider guard. Anthropic only serves `claude-*` models, so reject anything else.
    // OpenAI-compatible endpoints serve arbitrary names (gpt-*, o1, llama, qwen, …), so only reject an
    // obviously-Anthropic model string. Unknown providers are not constrained.
    internal static bool IsModelCompatibleWithProvider(string provider, string model)
    {
        var m = model.Trim();
        return provider.ToLowerInvariant() switch
        {
            "anthropic" => m.StartsWith("claude", StringComparison.OrdinalIgnoreCase),
            "openai" or "openai-compatible" or "openai-responses" => !m.StartsWith("claude", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private static string SerializeList(IReadOnlyList<string>? requested, string fallbackJson)
    {
        var values = requested is null ? DeserializeList(fallbackJson) : requested;
        var cleaned = values
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();
        return JsonSerializer.Serialize(cleaned, JsonOptions);
    }

    private static string[] DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string DisplayAgentLabel(string code, string displayName, string agentType)
    {
        var normalizedCode = NormalizeSlug(code);
        var normalizedType = NormalizeSlug(agentType);
        return normalizedCode.Contains("orchestrator", StringComparison.OrdinalIgnoreCase)
            || normalizedType.Contains("orchestrator", StringComparison.OrdinalIgnoreCase)
            || normalizedType.Contains("planner", StringComparison.OrdinalIgnoreCase)
            ? "Điều phối viên"
            : displayName;
    }

    private static string NormalizeSlug(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static AgentRuntimeConfig ReadRuntimeConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AgentRuntimeConfig();
        try
        {
            return JsonSerializer.Deserialize<AgentRuntimeConfig>(json, JsonOptions) ?? new AgentRuntimeConfig();
        }
        catch (JsonException)
        {
            return new AgentRuntimeConfig();
        }
    }

    private sealed class AgentRuntimeConfig
    {
        public string Provider { get; set; } = "claude";
        public string SystemPrompt { get; set; } = string.Empty;
        public double Temperature { get; set; } = 0.4;
        public int MaxTokens { get; set; } = 2048;
        public AgentTokenQuotaConfig TokenQuota { get; set; } = new();
        public string RouterTier { get; set; } = string.Empty;
        public AgentTokenAlertConfig TokenAlerts { get; set; } = new();
    }

    private sealed class AgentTokenQuotaConfig
    {
        public int MonthlyQuotaTokens { get; set; }
        public int AlertPercent { get; set; }
    }

    private sealed class AgentTokenAlertConfig
    {
        public bool Enabled { get; set; } = true;
        public int LowBalanceThresholdTokens { get; set; } = 500_000;
    }
}
