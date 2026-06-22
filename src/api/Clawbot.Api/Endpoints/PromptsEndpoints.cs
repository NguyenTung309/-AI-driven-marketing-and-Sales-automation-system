using System.Text.Json;
using Clawbot.Api.Middleware;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record PromptConfigStatsResponse(
    int TotalConfigs,
    int RunningConfigs,
    int PromptConfigured,
    int TokensLast7Days,
    decimal UsdLast7Days);

public sealed record PromptUsageLogResponse(
    Guid Id,
    string AgentCode,
    string Model,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    decimal Usd,
    DateTimeOffset CreatedAt);

public sealed record PromptConfigResponse(
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
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    int CallsLast7Days,
    int InputTokensLast7Days,
    int OutputTokensLast7Days,
    int TotalTokensLast7Days,
    decimal UsdLast7Days,
    IReadOnlyList<PromptUsageLogResponse> RecentUsage);

public sealed record PromptConfigListResponse(
    PromptConfigStatsResponse Stats,
    IReadOnlyList<PromptConfigResponse> Items);

public sealed record PromptConfigUpdateRequest(
    string? DisplayName,
    string? Model,
    string? Provider,
    string? SystemPrompt,
    double? Temperature,
    int? MaxTokens,
    IReadOnlyList<string>? SkillFiles,
    IReadOnlyList<string>? KbModules);

public sealed record PromptSandboxRequest(string Message, string? SystemPrompt);
public sealed record PromptSandboxResponse(Guid SessionId, string Reply, DateTimeOffset SentAt, int EstimatedTokens);

public static class PromptsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPrompts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prompts")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapGet("/configs", ListConfigsAsync);
        group.MapGet("/configs/{code}", GetConfigAsync);
        group.MapPut("/configs/{code}", UpdateConfigAsync);
        group.MapPost("/configs/{code}/sandbox", RunSandboxAsync);

        return group;
    }

    private static async Task<IResult> ListConfigsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var agents = await db.AgentConfigs
            .Where(agent => agent.DeletedAt == null)
            .OrderBy(agent => agent.AgentType)
            .ThenBy(agent => agent.Code)
            .ToListAsync(ct);

        var since = clock.UtcNow.AddDays(-7);
        var costs = await db.ClaudeCostLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(cost =>
                cost.TenantId == tenantId &&
                cost.AgentCode != ClaudeCostEntry.ReservationAgentCode &&
                cost.CreatedAt >= since &&
                cost.CreatedAt <= clock.UtcNow)
            .ToListAsync(ct);

        var lastRuns = await db.AgentSessions
            .AsNoTracking()
            .Where(session => session.AgentId.HasValue)
            .GroupBy(session => session.AgentId!.Value)
            .Select(group => new { AgentId = group.Key, LastRunAt = group.Max(session => session.StartedAt) })
            .ToDictionaryAsync(row => row.AgentId, row => (DateTimeOffset?)row.LastRunAt, ct);

        var items = agents
            .Select(agent => BuildConfigResponse(agent, costs, lastRuns.GetValueOrDefault(agent.Id), includeRecentUsage: false))
            .ToList();

        var stats = new PromptConfigStatsResponse(
            items.Count,
            items.Count(item => item.Status == "running"),
            items.Count(item => !string.IsNullOrWhiteSpace(item.SystemPrompt)),
            costs.Sum(cost => cost.InputTokens + cost.OutputTokens),
            costs.Sum(cost => cost.Usd));

        return Results.Ok(new PromptConfigListResponse(stats, items));
    }

    private static async Task<IResult> GetConfigAsync(
        string code,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var agent = await db.AgentConfigs.FirstOrDefaultAsync(item => item.Code == code, ct);
        if (agent is null) return Results.NotFound();

        var since = clock.UtcNow.AddDays(-7);
        var costs = await db.ClaudeCostLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(cost =>
                cost.TenantId == tenantId &&
                cost.AgentCode == agent.Code &&
                cost.AgentCode != ClaudeCostEntry.ReservationAgentCode &&
                cost.CreatedAt >= since &&
                cost.CreatedAt <= clock.UtcNow)
            .OrderByDescending(cost => cost.CreatedAt)
            .ToListAsync(ct);

        var lastRunAt = await db.AgentSessions
            .AsNoTracking()
            .Where(session => session.AgentId == agent.Id)
            .MaxAsync(session => (DateTimeOffset?)session.StartedAt, ct);

        return Results.Ok(BuildConfigResponse(agent, costs, lastRunAt, includeRecentUsage: true));
    }

    private static async Task<IResult> UpdateConfigAsync(
        string code,
        PromptConfigUpdateRequest request,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        var agent = await db.AgentConfigs.FirstOrDefaultAsync(item => item.Code == code, ct);
        if (agent is null) return Results.NotFound();

        var displayName = NormalizeText(request.DisplayName, agent.DisplayName, maxLength: 256);
        var model = NormalizeText(request.Model, agent.Model, maxLength: 128);
        if (string.IsNullOrWhiteSpace(displayName)) return Results.BadRequest(new { error = "display_name_required" });
        if (string.IsNullOrWhiteSpace(model)) return Results.BadRequest(new { error = "model_required" });

        var config = ReadRuntimeConfig(agent.ConfigJson);
        config.Provider = NormalizeText(request.Provider, config.Provider, maxLength: 64);
        config.SystemPrompt = (request.SystemPrompt ?? config.SystemPrompt).Trim();
        config.Temperature = request.Temperature.HasValue ? Math.Clamp(request.Temperature.Value, 0, 2) : config.Temperature;
        config.MaxTokens = request.MaxTokens.HasValue ? Math.Clamp(request.MaxTokens.Value, 128, 32000) : config.MaxTokens;

        agent.UpdateSettings(
            displayName,
            model,
            SerializeList(request.SkillFiles, agent.SkillFilesJson),
            SerializeList(request.KbModules, agent.KbModulesJson),
            JsonSerializer.Serialize(config, JsonOptions),
            clock.UtcNow);

        await db.SaveChangesAsync(ct);
        return await GetConfigAsync(code, db, tenants, clock, ct);
    }

    private static async Task<IResult> RunSandboxAsync(
        string code,
        PromptSandboxRequest request,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        IPiiRedactor pii,
        CancellationToken ct = default)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(request.Message)) return Results.BadRequest(new { error = "message_required" });

        var agent = await db.AgentConfigs.FirstOrDefaultAsync(item => item.Code == code, ct);
        if (agent is null) return Results.NotFound();

        var now = clock.UtcNow;
        var config = ReadRuntimeConfig(agent.ConfigJson);
        var effectivePrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? config.SystemPrompt
            : request.SystemPrompt.Trim();

        var message = request.Message.Trim();
        var redactedMessage = (await pii.RedactAsync(message, ct).ConfigureAwait(false)).RedactedText;
        var session = AgentSession.Start(tenant.TenantId, agent.Id, null, "Prompt configuration sandbox", now);
        session.AppendTrace("prompt-sandbox", agent.DisplayName, "system_prompt", RedactPromptForTrace(effectivePrompt), now);
        session.AppendTrace("prompt-sandbox", agent.DisplayName, "input", redactedMessage, now.AddMilliseconds(1));

        var reply = BuildSandboxReply(agent, config, effectivePrompt, redactedMessage);
        session.AppendTrace("prompt-sandbox", agent.DisplayName, "reply", reply, now.AddMilliseconds(2));
        session.Finish(now.AddMilliseconds(3));
        db.AgentSessions.Add(session);

        await db.SaveChangesAsync(ct);
        return Results.Ok(new PromptSandboxResponse(
            session.Id,
            reply,
            now.AddMilliseconds(2),
            EstimateTokens(effectivePrompt, request.Message, reply)));
    }

    private static PromptConfigResponse BuildConfigResponse(
        AgentConfig agent,
        IReadOnlyList<ClaudeCostEntry> costs,
        DateTimeOffset? lastRunAt,
        bool includeRecentUsage)
    {
        var config = ReadRuntimeConfig(agent.ConfigJson);
        var agentCosts = costs
            .Where(cost => string.Equals(cost.AgentCode, agent.Code, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var recentUsage = includeRecentUsage
            ? agentCosts
                .OrderByDescending(cost => cost.CreatedAt)
                .Take(5)
                .Select(cost => new PromptUsageLogResponse(
                    cost.Id,
                    cost.AgentCode,
                    cost.Model,
                    cost.InputTokens,
                    cost.OutputTokens,
                    cost.InputTokens + cost.OutputTokens,
                    cost.Usd,
                    cost.CreatedAt))
                .ToList()
            : [];

        return new PromptConfigResponse(
            agent.Code,
            agent.DisplayName,
            agent.AgentType,
            agent.Model,
            agent.Status,
            config.Provider,
            config.SystemPrompt,
            config.Temperature,
            config.MaxTokens,
            DeserializeList(agent.SkillFilesJson),
            DeserializeList(agent.KbModulesJson),
            agent.UpdatedAt,
            lastRunAt,
            agentCosts.Count,
            agentCosts.Sum(cost => cost.InputTokens),
            agentCosts.Sum(cost => cost.OutputTokens),
            agentCosts.Sum(cost => cost.InputTokens + cost.OutputTokens),
            agentCosts.Sum(cost => cost.Usd),
            recentUsage);
    }

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
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

    private static string RedactPromptForTrace(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return "No custom system prompt supplied.";
        var normalized = prompt.Trim();
        return normalized.Length <= 240 ? normalized : $"{normalized[..240]}...";
    }

    private static string BuildSandboxReply(AgentConfig agent, AgentRuntimeConfig config, string? systemPrompt, string message)
    {
        var promptHint = string.IsNullOrWhiteSpace(systemPrompt)
            ? "chưa có system prompt tùy chỉnh"
            : $"đang kiểm thử system prompt {Math.Min(systemPrompt.Length, 240)} ký tự";
        return $"{agent.DisplayName} đã nhận yêu cầu test \"{message}\" bằng provider {config.Provider}, model {agent.Model}, temperature {config.Temperature:0.##}, max {config.MaxTokens} tokens; {promptHint}.";
    }

    private static int EstimateTokens(params string?[] values)
    {
        var chars = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Sum(value => value!.Length);
        return Math.Max(1, (int)Math.Ceiling(chars / 4d));
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
