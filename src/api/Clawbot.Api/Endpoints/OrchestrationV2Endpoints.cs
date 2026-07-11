using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record OrchestrationV2RunRequest(string Goal, string? Source = null, bool DryRun = false);
// LlmConfigId: null = keep current binding, Guid.Empty = unbind, value = bind (validated against the tenant's llm_configs).
public sealed record OrchestrationV2AgentRequest(string Code, string DisplayName, string AgentType, string PersonaPrompt, bool IsOrchestratable = true, string? KbModuleCode = null, string? AllowedToolsJson = null, string? InputSchemaJson = null, Guid? LlmConfigId = null);
public sealed record OrchestrationV2ScheduleRequest(string Name, string GoalTemplate, string Cadence, string TimezoneId, DateTimeOffset? NextRunAt = null, bool RequiresApproval = false, string TriggerType = "cadence", string? EventKey = null);
public sealed record OrchestrationV2ControlRequest(string Action, string? Etag = null);
public sealed record OrchestrationV2UpdatePlanRequest(string PlanJson, string? Etag = null);
public sealed record OrchestrationV2ApproveRequest(string? Etag = null);

public sealed record OrchestrationV2AgentDto(Guid Id, string Code, string DisplayName, string AgentType, bool IsOrchestratable, int Version, string? KbModuleCode, string AllowedToolsJson = "[]", string InputSchemaJson = "{}", string PersonaPrompt = "", Guid? LlmConfigId = null);
public sealed record OrchestrationV2ScheduleDto(Guid Id, string Name, string GoalTemplate, string Cadence, string TimezoneId, DateTimeOffset NextRunAt, DateTimeOffset? LastRunAt, bool IsActive, bool RequiresApproval, string TriggerType = "cadence", string? EventKey = null);
public sealed record OrchestrationV2TraceDto(string TaskId, string AgentName, string Phase, string Message, DateTimeOffset OccurredAt);
// "Tự động xây dựng kế hoạch": orchestrator quét snapshot hệ thống, đề xuất kế hoạch định kỳ chưa trùng.
public sealed record OrchestrationPlanSuggestionDto(string Name, string Goal, string Cadence, string Reason);
public sealed record OrchestrationPlanSuggestionsResponse(IReadOnlyList<OrchestrationPlanSuggestionDto> Items, int SkippedDuplicates);
public sealed record OrchestrationV2MessageDto(Guid Id, string TaskId, string Intent, string Status, string PayloadJson, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt);

// SPEC-16 P3-2: derive per-agent UseCount (how many tasks in this session use that agent) and CurrentTaskId
// (the in-progress task for that agent, if any), mirrored from the legacy V1 endpoint mapping.
public sealed record OrchestrationV2TaskDto(
    string Id,
    string Agent,
    string Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyDictionary<string, string> Input,
    string Status,
    string? Output,
    string? Error,
    int UseCount = 0,
    string? CurrentTaskId = null);

public sealed record OrchestrationV2PlanDto(
    Guid SessionId,
    string Status,
    string Goal,
    bool RequiresApproval,
    bool CostBlocked,
    string? CostReason,
    int ReplanCount,
    string Etag,
    string PlanJson,
    IReadOnlyList<OrchestrationV2TaskDto> Tasks);

public sealed record OrchestrationV2RunDto(
    Guid SessionId,
    string Status,
    string Goal,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? ArchivedAt,
    bool RequiresApproval,
    bool CostBlocked,
    string? CostReason,
    int ReplanCount,
    string Etag,
    string PlanJson,
    IReadOnlyList<OrchestrationV2TaskDto> Tasks,
    IReadOnlyList<OrchestrationV2TraceDto> Traces,
    IReadOnlyList<OrchestrationV2MessageDto> Messages,
    decimal ActualCostUsd = 0m);

public static partial class OrchestrationV2Endpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOrchestrationV2(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orchestration/v2")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapPost("/runs", CreateRunAsync).RequirePermission("orchestration:run");
        group.MapGet("/runs", ListRunsAsync).RequirePermission("orchestration:view");
        group.MapGet("/runs/{id:guid}", GetRunAsync).RequirePermission("orchestration:view");
        group.MapPut("/runs/{id:guid}/plan", UpdateRunPlanAsync).RequirePermission("orchestration:run");
        group.MapPost("/runs/{id:guid}/approve", ApproveRunAsync).RequirePermission("orchestration:approve");
        group.MapPost("/runs/{id:guid}/control", ControlRunAsync).RequirePermission("orchestration:manage");
        group.MapPost("/runs/{id:guid}/archive", ArchiveRunAsync).RequirePermission("orchestration:manage");
        group.MapPost("/runs/{id:guid}/unarchive", UnarchiveRunAsync).RequirePermission("orchestration:manage");
        group.MapGet("/cost-summary", CostSummaryAsync).RequirePermission("orchestration:view");
        group.MapGet("/agents", ListAgentsAsync).RequirePermission("orchestration:view");
        group.MapPost("/agents", UpsertAgentAsync).RequirePermission("orchestration:manage");
        group.MapGet("/schedules", ListSchedulesAsync).RequirePermission("orchestration:view");
        group.MapPost("/schedules", CreateScheduleAsync).RequirePermission("orchestration:manage");
        // "Tự động xây dựng kế hoạch": LLM đọc snapshot hệ thống + kế hoạch hiện có -> đề xuất checklist.
        group.MapPost("/plan-suggestions", SuggestPlansAsync).RequirePermission("orchestration:run");
        group.MapPost("/schedules/{id:guid}/run-now", RunScheduleNowAsync).RequirePermission("orchestration:run");
        group.MapPost("/schedules/{id:guid}/pause", PauseScheduleAsync).RequirePermission("orchestration:manage");
        group.MapPost("/schedules/{id:guid}/activate", ActivateScheduleAsync).RequirePermission("orchestration:manage");
        return app;
    }

    private static async Task<IResult> CreateRunAsync(OrchestrationV2RunRequest body, ITenantAccessor tenants, Orchestrator.OrchestratorClient grpc, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Goal)) return Results.BadRequest(new { error = "goal_required" });
        var tenant = tenants.Require();
        // SPEC-16 P3-3: pass the initiating user's id so the run session + terminal notifications attribute to them.
        var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        try
        {
            var response = await grpc.SubmitAsync(new SubmitRequest { TenantId = tenant.TenantId.ToString("D"), Goal = body.Goal.Trim(), UserId = userId ?? string.Empty, DryRun = body.DryRun }, cancellationToken: ct).ConfigureAwait(false);
            return Results.Ok(new { sessionId = response.SessionId, status = response.Status });
        }
        catch (RpcException ex)
        {
            return ToGrpcResult(ex);
        }
    }

    private static async Task<IResult> ListRunsAsync(AppDbContext db, ITenantAccessor tenants, HttpContext http, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var showArchived = string.Equals(http.Request.Query["archived"], "true", StringComparison.OrdinalIgnoreCase);
        var query = db.AgentSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenant.TenantId)
            .Where(s => showArchived ? s.ArchivedAt != null : s.ArchivedAt == null)
            // Session nội bộ (auto-reply mỗi tin khách 1 phiên, sandbox chạy thử) không phải "phiên
            // điều phối" — tràn màn runs thành noise; trace của chúng xem ở màn agent tương ứng.
            .Where(s => s.Goal != "chat-reply" && s.Goal != "Agent configuration sandbox");
        // SPEC-16 P3-6: when the caller passes ?mine=true, filter to their own runs (URL-independent run list).
        if (string.Equals(http.Request.Query["mine"], "true", StringComparison.OrdinalIgnoreCase))
        {
            var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userId, out var uid) && uid != Guid.Empty)
                query = query.Where(s => s.UserId == uid);
        }
        var runs = await query.OrderByDescending(s => s.StartedAt)
            .Take(20)
            .Select(s => new { sessionId = s.Id, s.Status, s.Goal, s.StartedAt, s.FinishedAt, s.UserId })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = runs });
    }

    private static async Task<IResult> GetRunAsync(Guid id, AppDbContext db, ITenantAccessor tenants, Orchestrator.OrchestratorClient grpc, CancellationToken ct)
    {
        var tenant = tenants.Require();
        SessionResponse plan;
        try
        {
            plan = await grpc.GetPlanAsync(new SessionRef { TenantId = tenant.TenantId.ToString("D"), SessionId = id.ToString("D") }, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            return ToGrpcResult(ex);
        }

        // SessionResponse carries plan/status/etag but not lifecycle timestamps; pull those from EF alongside traces/messages.
        var timestamps = await db.AgentSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == id && s.TenantId == tenant.TenantId)
            .Select(s => new { s.StartedAt, s.FinishedAt, s.ArchivedAt })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var traces = await db.AgentTraces.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.SessionId == id)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new { t.TaskId, t.AgentName, t.Phase, t.Message, t.OccurredAt })
            .ToListAsync(ct).ConfigureAwait(false);
        var traceDtos = traces
            .Select(t => new OrchestrationV2TraceDto(t.TaskId ?? string.Empty, DisplayAgentLabel(t.AgentName), t.Phase ?? string.Empty, t.Message ?? string.Empty, t.OccurredAt))
            .ToArray();
        var messages = await db.AgentA2AMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.SessionId == id && m.TenantId == tenant.TenantId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new OrchestrationV2MessageDto(m.Id, m.TaskId, m.Intent, m.Status, m.PayloadJson, m.Error, m.CreatedAt, m.ProcessedAt))
            .ToListAsync(ct).ConfigureAwait(false);

        // Chi phí thực của phiên: tổng USD ledger gắn session này (bỏ dòng đặt chỗ chưa quyết toán).
        var actualCostUsd = await db.LlmCostLedger.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.SessionId == id
                && c.TenantId == tenant.TenantId
                && c.AgentCode != Clawbot.Domain.Agents.LlmCostEntry.ReservationAgentCode)
            .SumAsync(c => (decimal?)c.Usd, ct).ConfigureAwait(false) ?? 0m;

        return Results.Ok(new OrchestrationV2RunDto(
            id,
            plan.Status,
            plan.Goal,
            timestamps?.StartedAt ?? default,
            timestamps?.FinishedAt,
            timestamps?.ArchivedAt,
            plan.RequiresApproval,
            plan.CostBlocked,
            string.IsNullOrEmpty(plan.CostReason) ? null : plan.CostReason,
            plan.ReplanCount,
            plan.Etag,
            plan.PlanJson,
            ToTaskDtos(plan.Tasks).ToArray(),
            traceDtos,
            messages,
            actualCostUsd));
    }

    private static async Task<IResult> UpdateRunPlanAsync(Guid id, OrchestrationV2UpdatePlanRequest body, ITenantAccessor tenants, Orchestrator.OrchestratorClient grpc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.PlanJson)) return Results.BadRequest(new { error = "plan_json_required" });
        var tenant = tenants.Require();
        try
        {
            var response = await grpc.UpdatePlanAsync(new UpdatePlanRequest
            {
                TenantId = tenant.TenantId.ToString("D"),
                SessionId = id.ToString("D"),
                PlanJson = body.PlanJson,
                ExpectedEtag = body.Etag ?? string.Empty,
            }, cancellationToken: ct).ConfigureAwait(false);
            return Results.Ok(ToPlanDto(id, response));
        }
        catch (RpcException ex)
        {
            return ToGrpcResult(ex);
        }
    }

    private static async Task<IResult> ApproveRunAsync(Guid id, OrchestrationV2ApproveRequest? body, ITenantAccessor tenants, Orchestrator.OrchestratorClient grpc, CancellationToken ct)
    {
        var tenant = tenants.Require();
        try
        {
            var response = await grpc.ApproveAsync(new SessionRef
            {
                TenantId = tenant.TenantId.ToString("D"),
                SessionId = id.ToString("D"),
                ExpectedEtag = body?.Etag ?? string.Empty,
            }, cancellationToken: ct).ConfigureAwait(false);
            return Results.Ok(ToPlanDto(id, response));
        }
        catch (RpcException ex)
        {
            return ToGrpcResult(ex);
        }
    }

    private static OrchestrationV2PlanDto ToPlanDto(Guid sessionId, SessionResponse response) =>
        new(
            sessionId,
            response.Status,
            response.Goal,
            response.RequiresApproval,
            response.CostBlocked,
            string.IsNullOrEmpty(response.CostReason) ? null : response.CostReason,
            response.ReplanCount,
            response.Etag,
            response.PlanJson,
            ToTaskDtos(response.Tasks).ToArray());

    private static IEnumerable<OrchestrationV2TaskDto> ToTaskDtos(IReadOnlyList<PlannedTask> tasks)
    {
        var useCounts = tasks
            .GroupBy(t => t.Agent, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var currentByAgent = tasks
            .Where(t => string.Equals(t.Status, "running", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.Agent, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        return tasks.Select(task => new OrchestrationV2TaskDto(
            task.Id,
            task.Agent,
            task.Description,
            task.DependsOn.ToArray(),
            ParseInput(task.InputJson),
            task.Status,
            string.IsNullOrEmpty(task.Output) ? null : task.Output,
            string.IsNullOrEmpty(task.Error) ? null : task.Error,
            useCounts.TryGetValue(task.Agent, out var count) ? count : 1,
            currentByAgent.TryGetValue(task.Agent, out var curId) ? curId : null));
    }

    private static Dictionary<string, string> ParseInput(string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
            return new Dictionary<string, string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(inputJson, WebJsonOptions)
                ?? new Dictionary<string, string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static async Task<IResult> ControlRunAsync(Guid id, OrchestrationV2ControlRequest body, ITenantAccessor tenants, Orchestrator.OrchestratorClient grpc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Action)) return Results.BadRequest(new { error = "invalid_action" });
        var action = body.Action.Trim().ToLowerInvariant();
        if (action is not ("pause" or "resume" or "cancel")) return Results.BadRequest(new { error = "invalid_action" });
        var tenant = tenants.Require();
        try
        {
            var response = await grpc.ControlAsync(new ControlRequest { TenantId = tenant.TenantId.ToString("D"), SessionId = id.ToString("D"), Action = action, ExpectedEtag = body.Etag ?? string.Empty }, cancellationToken: ct).ConfigureAwait(false);
            return Results.Ok(new { sessionId = response.SessionId, status = response.Status });
        }
        catch (RpcException ex)
        {
            return ToGrpcResult(ex);
        }
    }

    private static async Task<IResult> ArchiveRunAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var session = await db.AgentSessions
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.TenantId, ct)
            .ConfigureAwait(false);
        if (session is null) return Results.NotFound(new { error = "session_not_found" });
        if (session.ArchivedAt is not null) return Results.Ok(new { sessionId = session.Id, session.Status, session.ArchivedAt });

        try
        {
            session.Archive(clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { sessionId = session.Id, session.Status, session.ArchivedAt });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = "session_not_archivable", message = ex.Message });
        }
    }

    private static async Task<IResult> ListAgentsAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var agents = await db.AgentDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.DeletedAt == null)
            .OrderBy(a => a.Code)
            .Select(a => new OrchestrationV2AgentDto(a.Id, a.Code, a.DisplayName, a.AgentType, a.IsOrchestratable, a.Version, a.KbModuleCode, a.AllowedToolsJson, a.InputSchemaJson, a.PersonaPrompt, a.LlmConfigId))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = agents });
    }

    private static async Task<IResult> UpsertAgentAsync(OrchestrationV2AgentRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, IPermissionResolver permissions, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.DisplayName) || string.IsNullOrWhiteSpace(body.PersonaPrompt))
            return Results.BadRequest(new { error = "agent_required_fields" });
        if (!string.IsNullOrWhiteSpace(body.KbModuleCode) && !await HasPermissionAsync(http, permissions, "kb:read", ct).ConfigureAwait(false))
            return Forbidden(http);
        // EARS[WHEN allowedTools or inputSchema is provided THE SYSTEM SHALL validate it parses as JSON before storing,
        // so a malformed allow-list cannot silently widen or narrow an agent's tool capabilities]
        var allowedTools = NormalizeAllowedTools(body.AllowedToolsJson);
        if (allowedTools is null)
            return Results.BadRequest(new { error = "invalid_allowed_tools_json" });
        // EARS[WHEN an admin sets an agent's allowed tools THE SYSTEM SHALL validate each tool name is known and the
        // admin holds that tool's required permission, so an admin cannot grant an agent a capability they lack]
        var validationError = await ValidateAllowedToolsAsync(allowedTools, http, permissions, ct).ConfigureAwait(false);
        if (validationError is not null)
            return Results.BadRequest(new { error = validationError });
        var inputSchema = NormalizeJsonObject(body.InputSchemaJson);
        if (inputSchema is null)
            return Results.BadRequest(new { error = "invalid_input_schema_json" });
        var tenant = tenants.Require();
        // EARS[WHEN an LLM binding is provided THE SYSTEM SHALL validate the config belongs to the tenant and is
        // active, so a definition cannot bind to another tenant's provider]
        if (body.LlmConfigId is { } bindId && bindId != Guid.Empty)
        {
            var configExists = await db.LlmConfigs.IgnoreQueryFilters()
                .AnyAsync(c => c.Id == bindId && c.TenantId == tenant.TenantId && c.IsActive, ct).ConfigureAwait(false);
            if (!configExists)
                return Results.BadRequest(new { error = "llm_config_not_found" });
        }
        var code = body.Code.Trim();
        var existing = await db.AgentDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.Code == code && a.DeletedAt == null, ct).ConfigureAwait(false);
        if (existing is null)
        {
            existing = AgentDefinition.Create(tenant.TenantId, code, body.DisplayName, body.AgentType, body.PersonaPrompt, clock.UtcNow,
                allowedToolsJson: allowedTools, inputSchemaJson: inputSchema, isOrchestratable: body.IsOrchestratable, kbModuleCode: body.KbModuleCode);
            db.AgentDefinitions.Add(existing);
        }

        var llmConfigId = body.LlmConfigId is null
            ? existing.LlmConfigId
            : (body.LlmConfigId == Guid.Empty ? null : body.LlmConfigId);
        existing.UpdateDefinition(body.DisplayName, body.AgentType, body.PersonaPrompt, allowedTools, inputSchema,
            existing.OutputSchemaJson, existing.MemoryScope, llmConfigId, body.IsOrchestratable, clock.UtcNow, body.KbModuleCode);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new OrchestrationV2AgentDto(existing.Id, existing.Code, existing.DisplayName, existing.AgentType, existing.IsOrchestratable, existing.Version, existing.KbModuleCode, existing.AllowedToolsJson, existing.InputSchemaJson, existing.PersonaPrompt, existing.LlmConfigId));
    }

    // ponytail: validate JSON shape without a schema lib; allowedTools must be a JSON array, inputSchema a JSON object.
    internal static string? NormalizeAllowedTools(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "[]";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? raw.Trim() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // SPEC-16 P4-3: validate each allowed tool name is known and the admin holds the tool's required permission.
    internal static async Task<string?> ValidateAllowedToolsAsync(string allowedToolsJson, HttpContext http, IPermissionResolver permissions, CancellationToken ct)
    {
        string[] names;
        try
        {
            names = System.Text.Json.JsonSerializer.Deserialize<string[]>(allowedToolsJson, WebJsonOptions) ?? [];
        }
        catch (System.Text.Json.JsonException) { return "invalid_allowed_tools_json"; }

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!ToolRegistryFactory.KnownTools.TryGetValue(name.Trim(), out var meta))
                return $"unknown_tool:{name}";
            if (!string.IsNullOrEmpty(meta.Permission) && !await HasPermissionAsync(http, permissions, meta.Permission, ct).ConfigureAwait(false))
                return $"tool_permission_denied:{name}:{meta.Permission}";
        }
        return null;
    }

    internal static string? NormalizeJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object ? raw.Trim() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static async Task<IResult> ListSchedulesAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var schedules = await db.AgentSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenant.TenantId && s.DeletedAt == null)
            .OrderBy(s => s.NextRunAt)
            .Select(s => ToScheduleDto(s))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = schedules });
    }

    // "Tự động xây dựng kế hoạch": quét snapshot dữ liệu tenant -> LLM (binding orchestrator) đề xuất
    // 3-6 kế hoạch định kỳ -> lọc trùng với schedules hiện có (LLM được nhắc tránh + BE lọc lại lần cuối).
    private static async Task<IResult> SuggestPlansAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        Clawbot.Agents.Core.Chat.IClaudeChatClient chatClient,
        Clawbot.Agents.Core.Chat.ILlmCallScope llmScope,
        IClock clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var tenantId = tenant.TenantId;

        // Snapshot gọn — chỉ số liệu tổng hợp, không kéo nội dung (khỏi dính PII).
        var leadsByStage = await db.Leads.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .GroupBy(l => l.Stage).Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var openConversations = await db.Conversations.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId && c.Status == "open", ct).ConfigureAwait(false);
        var escalatedConversations = await db.Conversations.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId && c.Status == "escalated", ct).ConfigureAwait(false);
        var contentByStatus = await db.ContentItems.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DeletedAt == null)
            .GroupBy(i => i.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var existingSchedules = await db.AgentSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.DeletedAt == null)
            .Select(s => new { s.Name, s.GoalTemplate })
            .ToListAsync(ct).ConfigureAwait(false);
        var boundAgents = await db.AgentDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DeletedAt == null && a.IsOrchestratable)
            .Select(a => a.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var snapshot = new System.Text.StringBuilder();
        snapshot.AppendLine("## Snapshot hệ thống");
        snapshot.AppendLine(inv, $"- Leads theo giai đoạn: {string.Join(", ", leadsByStage.Select(x => $"{x.Stage}={x.Count}"))}");
        snapshot.AppendLine(inv, $"- Hội thoại: open={openConversations}, cần hỗ trợ={escalatedConversations}");
        snapshot.AppendLine(inv, $"- Nội dung: {string.Join(", ", contentByStatus.Select(x => $"{x.Status}={x.Count}"))}");
        snapshot.AppendLine(inv, $"- Agent khả dụng: {string.Join(", ", boundAgents)}");
        snapshot.AppendLine();
        snapshot.AppendLine("## Kế hoạch ĐÃ CÓ (tuyệt đối không đề xuất trùng/na ná)");
        if (existingSchedules.Count == 0) snapshot.AppendLine("- (chưa có)");
        foreach (var s in existingSchedules) snapshot.AppendLine(inv, $"- {s.Name}: {s.GoalTemplate}");

        var system = Clawbot.Agents.Core.AgentPromptDefaults.Compose(
            Clawbot.Agents.Core.AgentPromptDefaults.DefaultFor("orchestrator"))
            + "\n\n# Định dạng trả lời (bắt buộc)\n"
            + "Chỉ trả về đúng một JSON object, không thêm chữ nào khác: "
            + """{"suggestions":[{"name":"Chấm điểm khách tiềm năng","goal":"Chấm điểm toàn bộ khách tiềm năng theo mức tương tác gần đây và nguồn, chọn 5 khách ưu tiên chăm trong tuần","cadence":"daily|weekly|monthly|quarterly","reason":"vì sao hệ thống cần, dựa trên snapshot"}]}"""
            + "\nJSON thô — KHÔNG bọc trong ``` hay thêm lời dẫn/giải thích trước sau."
            + "\nBẮT BUỘC: toàn bộ giá trị name, goal, reason viết bằng TIẾNG VIỆT 100% (tuyệt đối không dùng tiếng Anh); chỉ cadence giữ nguyên daily|weekly|monthly|quarterly.";
        var user = snapshot
            + "\n## Nhóm kế hoạch NỀN TẢNG (ưu tiên đề xuất TRƯỚC nếu danh sách đã có còn thiếu)"
            + "\n- Tự động sáng tạo + đăng bài định kỳ theo lịch (tuyển sinh, khóa học, thương hiệu)"
            + "\n- Tự động chấm điểm + phân loại khách hàng tiềm năng, chọn nhóm ưu tiên chăm"
            + "\n- Tự động nhắn lại chăm sóc khách tiềm năng lâu không tương tác (khơi lại quan tâm theo chuỗi định kỳ)"
            + "\n- Tự động cải thiện kho tri thức: rà hội thoại tìm câu hỏi AI trả lời kém, đề xuất bổ sung KB"
            + "\n- Nghiên cứu chủ đề/đối thủ định kỳ để lên brief nội dung"
            + "\n- Báo cáo KPI vận hành định kỳ cho quản lý"
            + "\n"
            + "\nDựa trên snapshot, đề xuất CÀNG NHIỀU kế hoạch ĐỊNH KỲ mới càng tốt (tối thiểu 8, không giới hạn trên): "
            + "điền đủ nhóm NỀN TẢNG còn thiếu trước, rồi mở rộng sang kế hoạch chuyên sâu (giám sát cảm xúc, "
            + "cân bằng tải, thử nghiệm nội dung, chăm khách cũ...). Sắp xếp kết quả: nền tảng trước, chuyên sâu sau. "
            + "Mỗi kế hoạch phải khả thi với danh sách agent khả dụng ở trên và khác hẳn danh sách đã có.";

        // LLM chập chờn là bản chất (lúc trả JSON sạch, lúc kèm lời dẫn/bị cạn token giữa chừng) —
        // retry 1 lần trước khi trả lỗi cho người dùng.
        var parsed = new List<OrchestrationPlanSuggestionDto>();
        var replyText = string.Empty;
        for (var attempt = 1; attempt <= 2 && parsed.Count == 0; attempt++)
        {
            try
            {
                using var _ = llmScope.Begin(tenantId, "orchestrator", clock.UtcNow);
                var reply = await chatClient.CompleteAsync(system, Array.Empty<Clawbot.Agents.Core.Chat.ChatTurn>(), user, ct).ConfigureAwait(false);
                replyText = reply.Text;
            }
            catch (Clawbot.Agents.Core.Chat.LlmConfigNotConfiguredException)
            {
                return Results.BadRequest(new { error = "llm_config_not_configured" });
            }

            parsed = ParseSuggestions(replyText);
            if (parsed.Count == 0)
            {
                // Log reply thô (cắt ngắn) — snapshot chỉ chứa số liệu tổng hợp, không PII.
                var suggestLogger = loggerFactory.CreateLogger(nameof(OrchestrationV2Endpoints));
                LogSuggestionParseFailed(suggestLogger, tenantId, replyText.Length,
                    replyText.Length > 800 ? replyText[..800] : replyText);
            }
        }

        if (parsed.Count == 0)
        {
            var preview = replyText.Length > 300 ? replyText[..300] + "..." : replyText;
            return Results.UnprocessableEntity(new
            {
                error = "suggestion_parse_failed",
                message = "Orchestrator không trả về đề xuất hợp lệ sau 2 lần thử — thử lại hoặc kiểm tra cấu hình model.",
                detail = string.IsNullOrWhiteSpace(preview) ? "(model trả về rỗng — nghi hết token output; bỏ trống 'Số token tối đa' trong cấu hình LLM)" : preview,
            });
        }

        // Lọc trùng lần cuối: tên chuẩn hóa trùng, hoặc goal giao tokens >= 60% với kế hoạch đã có.
        var existingNorms = existingSchedules
            .Select(s => (Name: NormalizePlanText(s.Name), GoalTokens: PlanTokens(s.GoalTemplate)))
            .ToList();
        var items = new List<OrchestrationPlanSuggestionDto>();
        var skipped = 0;
        foreach (var suggestion in parsed)
        {
            var normName = NormalizePlanText(suggestion.Name);
            var goalTokens = PlanTokens(suggestion.Goal);
            var duplicate = existingNorms.Any(e =>
                e.Name == normName
                || (goalTokens.Count > 0 && e.GoalTokens.Count > 0
                    && (double)goalTokens.Intersect(e.GoalTokens).Count() / Math.Min(goalTokens.Count, e.GoalTokens.Count) >= 0.6));
            if (duplicate) { skipped++; continue; }
            items.Add(suggestion);
        }

        return Results.Ok(new OrchestrationPlanSuggestionsResponse(items, skipped));
    }

    internal static List<OrchestrationPlanSuggestionDto> ParseSuggestions(string text)
    {
        var result = new List<OrchestrationPlanSuggestionDto>();
        var candidate = ExtractJsonCandidate(text);
        if (candidate is null) return result;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(candidate);
            var root = doc.RootElement;
            System.Text.Json.JsonElement arr;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                arr = root; // model trả thẳng mảng suggestions
            else if (root.TryGetProperty("suggestions", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Array)
                arr = s;
            else
                return result;

            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var name = el.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
                var goal = el.TryGetProperty("goal", out var g) ? g.GetString()?.Trim() : null;
                var cadence = el.TryGetProperty("cadence", out var c) ? c.GetString()?.Trim().ToLowerInvariant() : null;
                var reason = el.TryGetProperty("reason", out var r) ? r.GetString()?.Trim() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(goal)) continue;
                if (cadence is null || !IsKnownCadence(cadence)) cadence = "weekly";
                result.Add(new OrchestrationPlanSuggestionDto(name, goal, cadence, reason));
            }
        }
        catch (System.Text.Json.JsonException) { /* fail-safe: caller trả suggestion_parse_failed khi rỗng */ }
        return result;
    }

    // Model hay bọc ```json ...``` hoặc thêm lời dẫn dù đã cấm — bóc fence trước, rồi object, rồi mảng.
    private static string? ExtractJsonCandidate(string text)
    {
        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = text.IndexOf('\n', fenceStart);
            var fenceEnd = contentStart > 0 ? text.IndexOf("```", contentStart, StringComparison.Ordinal) : -1;
            if (contentStart > 0 && fenceEnd > contentStart)
                text = text[(contentStart + 1)..fenceEnd];
        }

        var objStart = text.IndexOf('{');
        var objEnd = text.LastIndexOf('}');
        if (objStart >= 0 && objEnd > objStart) return text[objStart..(objEnd + 1)];

        var arrStart = text.IndexOf('[');
        var arrEnd = text.LastIndexOf(']');
        if (arrStart >= 0 && arrEnd > arrStart) return text[arrStart..(arrEnd + 1)];

        return null;
    }

    [LoggerMessage(EventId = 7401, Level = LogLevel.Warning,
        Message = "Plan suggestions parse failed for tenant {TenantId}: replyLength={ReplyLength} replyPreview={ReplyPreview}")]
    private static partial void LogSuggestionParseFailed(ILogger logger, Guid tenantId, int replyLength, string replyPreview);

    private static string NormalizePlanText(string text) =>
        string.Join(' ', PlanTokens(text));

    private static HashSet<string> PlanTokens(string text) =>
        new(text.ToLowerInvariant()
            .Split([' ', ',', '.', ':', ';', '-', '_', '/', '(', ')', '"', '\'', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2), StringComparer.Ordinal);

    private static async Task<IResult> CreateScheduleAsync(OrchestrationV2ScheduleRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.GoalTemplate)) return Results.BadRequest(new { error = "schedule_required_fields" });
        if (!IsKnownCadence(body.Cadence)) return Results.BadRequest(new { error = "invalid_cadence" });
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(body.TimezoneId); }
        catch (TimeZoneNotFoundException) { return Results.BadRequest(new { error = "invalid_timezone" }); }
        catch (InvalidTimeZoneException) { return Results.BadRequest(new { error = "invalid_timezone" }); }
        // C2: event-triggered schedules — event key phải nằm trong catalog đã nối dây dispatcher.
        var triggerType = string.IsNullOrWhiteSpace(body.TriggerType) ? "cadence" : body.TriggerType.Trim().ToLowerInvariant();
        if (triggerType is not ("cadence" or "event")) return Results.BadRequest(new { error = "invalid_trigger_type" });
        if (triggerType == "event"
            && !Clawbot.SharedKernel.Orchestration.ScheduleEventKeys.All.Contains(body.EventKey?.Trim().ToLowerInvariant() ?? string.Empty))
        {
            return Results.BadRequest(new { error = "invalid_event_key" });
        }
        var tenant = tenants.Require();
        // Event schedules ngủ tới khi sự kiện kéo NextRunAt về; cadence schedules chạy ngay mốc đầu.
        var nextRunAt = triggerType == "event" ? DateTimeOffset.MaxValue : body.NextRunAt ?? clock.UtcNow;
        var schedule = AgentSchedule.Create(tenant.TenantId, body.Name, body.GoalTemplate, body.Cadence, null, body.TimezoneId, nextRunAt, body.RequiresApproval, clock.UtcNow, triggerType: triggerType, eventKey: body.EventKey);
        db.AgentSchedules.Add(schedule);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToScheduleDto(schedule));
    }

    private static async Task<IResult> RunScheduleNowAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var schedule = await db.AgentSchedules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.TenantId && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null) return Results.NotFound(new { error = "schedule_not_found" });

        var now = clock.UtcNow;
        schedule.UpdateSchedule(schedule.Name, schedule.GoalTemplate, schedule.Cadence, schedule.CronExpression, schedule.TimezoneId, now, schedule.RequiresApproval, schedule.OverlapPolicy, schedule.MisfirePolicy, schedule.ApprovalPolicyJson, now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Accepted($"/api/orchestration/v2/schedules/{id}", new { status = "queued", nextRunAt = schedule.NextRunAt });
    }

    private static async Task<IResult> PauseScheduleAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var schedule = await db.AgentSchedules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.TenantId && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null) return Results.NotFound(new { error = "schedule_not_found" });

        schedule.Pause(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToScheduleDto(schedule));
    }

    private static async Task<IResult> ActivateScheduleAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var schedule = await db.AgentSchedules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.TenantId && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null) return Results.NotFound(new { error = "schedule_not_found" });

        schedule.Activate(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToScheduleDto(schedule));
    }

    private static async Task<IResult> UnarchiveRunAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var session = await db.AgentSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.TenantId, ct)
            .ConfigureAwait(false);
        if (session is null) return Results.NotFound(new { error = "session_not_found" });

        session.Unarchive();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { sessionId = session.Id, session.Status, session.ArchivedAt });
    }

    // B5: chi tiêu LLM tháng này + hạn mức — cùng nguồn số liệu với OrchestratorCostGuard (LlmCostLedger),
    // để người duyệt thấy guardrail thật ngay tại điểm phê duyệt.
    private static async Task<IResult> CostSummaryAsync(
        Clawbot.Agents.Core.Skills.Ops.ILlmCostTracker tracker,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var summary = await tracker.SummaryAsync(tenant.TenantId, clock.UtcNow, ct).ConfigureAwait(false);
        return Results.Ok(new { monthToDateUsd = summary.MonthToDateUsd, capUsd = summary.CapUsd });
    }

    private static OrchestrationV2ScheduleDto ToScheduleDto(AgentSchedule s) =>
        new(s.Id, s.Name, s.GoalTemplate, s.Cadence, s.TimezoneId, s.NextRunAt, s.LastRunAt, s.IsActive, s.RequiresApproval, s.TriggerType, s.EventKey);

    private static string DisplayAgentLabel(string? agentName)
    {
        var value = (agentName ?? string.Empty).Trim();
        return value.Equals("orchestrator", StringComparison.OrdinalIgnoreCase)
            ? "Điều phối viên"
            : value;
    }

    private static bool IsKnownCadence(string cadence) => cadence.Trim().ToLowerInvariant() is "daily" or "weekly" or "monthly" or "quarterly";

    private static async Task<bool> HasPermissionAsync(HttpContext http, IPermissionResolver permissions, string code, CancellationToken ct)
    {
        if (http.User.HasClaim("perm", code)) return true;
        if (!Guid.TryParse(http.User.FindFirst("role_id")?.Value, out var roleId) || roleId == Guid.Empty) return false;
        var granted = await permissions.GetPermissionsAsync(roleId, ct).ConfigureAwait(false);
        return granted.Contains(code);
    }

    private static IResult Forbidden(HttpContext http) =>
        Results.Json(new { errorCode = "forbidden", message = "Không có quyền", requestId = http.TraceIdentifier }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ToGrpcResult(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.NotFound => Results.NotFound(new { error = ex.Status.Detail }),
        StatusCode.InvalidArgument => Results.BadRequest(new { error = ex.Status.Detail }),
        StatusCode.FailedPrecondition => Results.Conflict(new { error = ex.Status.Detail }),
        _ => Results.Problem(ex.Status.Detail, statusCode: StatusCodes.Status502BadGateway),
    };
}
