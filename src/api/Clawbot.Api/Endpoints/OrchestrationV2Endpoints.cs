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

public sealed record OrchestrationV2RunRequest(string Goal, string? Source = null);
public sealed record OrchestrationV2AgentRequest(string Code, string DisplayName, string AgentType, string PersonaPrompt, bool IsOrchestratable = true, string? KbModuleCode = null);
public sealed record OrchestrationV2ScheduleRequest(string Name, string GoalTemplate, string Cadence, string TimezoneId, DateTimeOffset? NextRunAt = null, bool RequiresApproval = false);
public sealed record OrchestrationV2ControlRequest(string Action, string? Etag = null);

public sealed record OrchestrationV2AgentDto(Guid Id, string Code, string DisplayName, string AgentType, bool IsOrchestratable, int Version, string? KbModuleCode);
public sealed record OrchestrationV2ScheduleDto(Guid Id, string Name, string GoalTemplate, string Cadence, string TimezoneId, DateTimeOffset NextRunAt, DateTimeOffset? LastRunAt, bool IsActive, bool RequiresApproval);
public sealed record OrchestrationV2TraceDto(string TaskId, string AgentName, string Phase, string Message, DateTimeOffset OccurredAt);
public sealed record OrchestrationV2MessageDto(Guid Id, string TaskId, string Intent, string Status, string PayloadJson, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt);
public sealed record OrchestrationV2RunDto(Guid SessionId, string Status, string Goal, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, IReadOnlyList<OrchestrationV2TraceDto> Traces, IReadOnlyList<OrchestrationV2MessageDto> Messages);

public static class OrchestrationV2Endpoints
{
    public static IEndpointRouteBuilder MapOrchestrationV2(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orchestration/v2")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapPost("/runs", CreateRunAsync).RequirePermission("orchestration:run");
        group.MapGet("/runs", ListRunsAsync).RequirePermission("orchestration:view");
        group.MapGet("/runs/{id:guid}", GetRunAsync).RequirePermission("orchestration:view");
        group.MapPost("/runs/{id:guid}/control", ControlRunAsync).RequirePermission("orchestration:manage");
        group.MapGet("/agents", ListAgentsAsync).RequirePermission("orchestration:view");
        group.MapPost("/agents", UpsertAgentAsync).RequirePermission("orchestration:manage");
        group.MapGet("/schedules", ListSchedulesAsync).RequirePermission("orchestration:view");
        group.MapPost("/schedules", CreateScheduleAsync).RequirePermission("orchestration:manage");
        group.MapPost("/schedules/{id:guid}/run-now", RunScheduleNowAsync).RequirePermission("orchestration:run");
        return app;
    }

    private static async Task<IResult> CreateRunAsync(OrchestrationV2RunRequest body, ITenantAccessor tenants, Orchestrator.OrchestratorClient grpc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Goal)) return Results.BadRequest(new { error = "goal_required" });
        var tenant = tenants.Require();
        try
        {
            var response = await grpc.SubmitAsync(new SubmitRequest { TenantId = tenant.TenantId.ToString("D"), Goal = body.Goal.Trim() }, cancellationToken: ct).ConfigureAwait(false);
            return Results.Ok(new { sessionId = response.SessionId, status = response.Status });
        }
        catch (RpcException ex)
        {
            return ToGrpcResult(ex);
        }
    }

    private static async Task<IResult> ListRunsAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var runs = await db.AgentSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenant.TenantId)
            .OrderByDescending(s => s.StartedAt)
            .Take(20)
            .Select(s => new { sessionId = s.Id, s.Status, s.Goal, s.StartedAt, s.FinishedAt })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = runs });
    }

    private static async Task<IResult> GetRunAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var session = await db.AgentSessions.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.TenantId, ct).ConfigureAwait(false);
        if (session is null) return Results.NotFound(new { error = "session_not_found" });
        var traces = await db.AgentTraces.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.SessionId == id)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new OrchestrationV2TraceDto(t.TaskId ?? string.Empty, t.AgentName ?? string.Empty, t.Phase ?? string.Empty, t.Message ?? string.Empty, t.OccurredAt))
            .ToListAsync(ct).ConfigureAwait(false);
        var messages = await db.AgentA2AMessages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.SessionId == id && m.TenantId == tenant.TenantId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new OrchestrationV2MessageDto(m.Id, m.TaskId, m.Intent, m.Status, m.PayloadJson, m.Error, m.CreatedAt, m.ProcessedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new OrchestrationV2RunDto(session.Id, session.Status, session.Goal ?? string.Empty, session.StartedAt, session.FinishedAt, traces, messages));
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

    private static async Task<IResult> ListAgentsAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var agents = await db.AgentDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.DeletedAt == null)
            .OrderBy(a => a.Code)
            .Select(a => new OrchestrationV2AgentDto(a.Id, a.Code, a.DisplayName, a.AgentType, a.IsOrchestratable, a.Version, a.KbModuleCode))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = agents });
    }

    private static async Task<IResult> UpsertAgentAsync(OrchestrationV2AgentRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, IPermissionResolver permissions, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.DisplayName) || string.IsNullOrWhiteSpace(body.PersonaPrompt))
            return Results.BadRequest(new { error = "agent_required_fields" });
        if (!string.IsNullOrWhiteSpace(body.KbModuleCode) && !await HasPermissionAsync(http, permissions, "kb:read", ct).ConfigureAwait(false))
            return Forbidden(http);
        var tenant = tenants.Require();
        var code = body.Code.Trim();
        var existing = await db.AgentDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.Code == code && a.DeletedAt == null, ct).ConfigureAwait(false);
        if (existing is null)
        {
            existing = AgentDefinition.Create(tenant.TenantId, code, body.DisplayName, body.AgentType, body.PersonaPrompt, clock.UtcNow,
                isOrchestratable: body.IsOrchestratable, kbModuleCode: body.KbModuleCode);
            db.AgentDefinitions.Add(existing);
        }
        else
        {
            existing.UpdateDefinition(body.DisplayName, body.AgentType, body.PersonaPrompt, existing.AllowedToolsJson, existing.InputSchemaJson,
                existing.OutputSchemaJson, existing.MemoryScope, existing.LlmConfigId, body.IsOrchestratable, clock.UtcNow, body.KbModuleCode);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new OrchestrationV2AgentDto(existing.Id, existing.Code, existing.DisplayName, existing.AgentType, existing.IsOrchestratable, existing.Version, existing.KbModuleCode));
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

    private static async Task<IResult> CreateScheduleAsync(OrchestrationV2ScheduleRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.GoalTemplate)) return Results.BadRequest(new { error = "schedule_required_fields" });
        if (!IsKnownCadence(body.Cadence)) return Results.BadRequest(new { error = "invalid_cadence" });
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(body.TimezoneId); }
        catch (TimeZoneNotFoundException) { return Results.BadRequest(new { error = "invalid_timezone" }); }
        catch (InvalidTimeZoneException) { return Results.BadRequest(new { error = "invalid_timezone" }); }
        var tenant = tenants.Require();
        var schedule = AgentSchedule.Create(tenant.TenantId, body.Name, body.GoalTemplate, body.Cadence, null, body.TimezoneId, body.NextRunAt ?? clock.UtcNow, body.RequiresApproval, clock.UtcNow);
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

    private static OrchestrationV2ScheduleDto ToScheduleDto(AgentSchedule s) =>
        new(s.Id, s.Name, s.GoalTemplate, s.Cadence, s.TimezoneId, s.NextRunAt, s.LastRunAt, s.IsActive, s.RequiresApproval);

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
