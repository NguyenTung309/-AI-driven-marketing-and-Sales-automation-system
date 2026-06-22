using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record OrchestrationSubmitRequest(string Goal);
public sealed record OrchestrationUpdatePlanRequest(string PlanJson, string? Etag);
public sealed record OrchestrationControlRequest(string? Etag);

public sealed record OrchestrationTaskDto(
    string Id,
    string Agent,
    string Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyDictionary<string, string> Input,
    string Status,
    string? Output,
    string? Error);

public sealed record OrchestrationSessionDto(
    string SessionId,
    string Status,
    bool RequiresApproval,
    string Goal,
    bool CostBlocked,
    string? CostReason,
    int ReplanCount,
    string Etag,
    string PlanJson,
    IReadOnlyList<OrchestrationTaskDto> Tasks);

public sealed record OrchestrationTraceDto(
    string TaskId,
    string AgentName,
    string Phase,
    string Message,
    DateTimeOffset OccurredAt);

public static class OrchestrationEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOrchestration(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/orchestration")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapPost("/submit", SubmitAsync).RequirePermission("orchestration:run");
        grp.MapGet("/{sessionId}", GetAsync).RequirePermission("orchestration:view");
        grp.MapGet("/{sessionId}/trace", TraceAsync).RequirePermission("orchestration:view");
        grp.MapPut("/{sessionId}/plan", UpdatePlanAsync).RequirePermission("orchestration:run");
        grp.MapPost("/{sessionId}/approve", ApproveAsync).RequirePermission("orchestration:approve");
        grp.MapPost("/{sessionId}/pause", (string sessionId, OrchestrationControlRequest? body, ITenantAccessor t, Orchestrator.OrchestratorClient g, CancellationToken ct)
            => ControlAsync(sessionId, "pause", body, t, g, ct)).RequirePermission("orchestration:manage");
        grp.MapPost("/{sessionId}/resume", (string sessionId, OrchestrationControlRequest? body, ITenantAccessor t, Orchestrator.OrchestratorClient g, CancellationToken ct)
            => ControlAsync(sessionId, "resume", body, t, g, ct)).RequirePermission("orchestration:manage");
        grp.MapPost("/{sessionId}/cancel", (string sessionId, OrchestrationControlRequest? body, ITenantAccessor t, Orchestrator.OrchestratorClient g, CancellationToken ct)
            => ControlAsync(sessionId, "cancel", body, t, g, ct)).RequirePermission("orchestration:manage");

        return app;
    }

    private static async Task<IResult> SubmitAsync(
        OrchestrationSubmitRequest body,
        ITenantAccessor tenants,
        Orchestrator.OrchestratorClient grpc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.Goal))
            return Results.BadRequest(new { error = "goal_required" });

        var tenant = tenants.Require();
        return await CallAsync(() => grpc.SubmitAsync(new SubmitRequest
        {
            TenantId = tenant.TenantId.ToString(),
            Goal = body.Goal,
        }, cancellationToken: ct));
    }

    private static async Task<IResult> GetAsync(
        string sessionId,
        ITenantAccessor tenants,
        Orchestrator.OrchestratorClient grpc,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        return await CallAsync(() => grpc.GetPlanAsync(new SessionRef
        {
            TenantId = tenant.TenantId.ToString(),
            SessionId = sessionId,
        }, cancellationToken: ct));
    }

    private static async Task<IResult> TraceAsync(
        string sessionId,
        ITenantAccessor tenants,
        AppDbContext db,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (!Guid.TryParse(sessionId, out var parsedSessionId) || parsedSessionId == Guid.Empty)
            return Results.BadRequest(new { error = "session_id_required" });

        var exists = await db.AgentSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(session => session.Id == parsedSessionId && session.TenantId == tenant.TenantId, ct)
            .ConfigureAwait(false);
        if (!exists)
            return Results.NotFound(new { error = "session_not_found" });

        var traces = await db.AgentTraces
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(trace => trace.SessionId == parsedSessionId)
            .OrderBy(trace => trace.OccurredAt)
            .Select(trace => new OrchestrationTraceDto(
                trace.TaskId ?? string.Empty,
                trace.AgentName ?? string.Empty,
                trace.Phase ?? string.Empty,
                trace.Message ?? string.Empty,
                trace.OccurredAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Results.Ok(new { items = traces });
    }

    private static async Task<IResult> UpdatePlanAsync(
        string sessionId,
        OrchestrationUpdatePlanRequest body,
        ITenantAccessor tenants,
        Orchestrator.OrchestratorClient grpc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.PlanJson))
            return Results.BadRequest(new { error = "plan_json_required" });

        var tenant = tenants.Require();
        return await CallAsync(() => grpc.UpdatePlanAsync(new UpdatePlanRequest
        {
            TenantId = tenant.TenantId.ToString(),
            SessionId = sessionId,
            PlanJson = body.PlanJson,
            ExpectedEtag = body.Etag ?? string.Empty,
        }, cancellationToken: ct));
    }

    private static async Task<IResult> ApproveAsync(
        string sessionId,
        OrchestrationControlRequest? body,
        ITenantAccessor tenants,
        Orchestrator.OrchestratorClient grpc,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        return await CallAsync(() => grpc.ApproveAsync(new SessionRef
        {
            TenantId = tenant.TenantId.ToString(),
            SessionId = sessionId,
            ExpectedEtag = body?.Etag ?? string.Empty,
        }, cancellationToken: ct));
    }

    private static async Task<IResult> ControlAsync(
        string sessionId,
        string action,
        OrchestrationControlRequest? body,
        ITenantAccessor tenants,
        Orchestrator.OrchestratorClient grpc,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        return await CallAsync(() => grpc.ControlAsync(new ControlRequest
        {
            TenantId = tenant.TenantId.ToString(),
            SessionId = sessionId,
            Action = action,
            ExpectedEtag = body?.Etag ?? string.Empty,
        }, cancellationToken: ct));
    }

    private static async Task<IResult> CallAsync(Func<AsyncUnaryCall<SessionResponse>> call)
    {
        try
        {
            var response = await call().ConfigureAwait(false);
            return Results.Ok(ToDto(response));
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.NotFound => Results.NotFound(new { error = ex.Status.Detail }),
                StatusCode.InvalidArgument => Results.BadRequest(new { error = ex.Status.Detail }),
                StatusCode.FailedPrecondition => Results.Conflict(new { error = ex.Status.Detail }),
                _ => Results.Problem(ex.Status.Detail, statusCode: StatusCodes.Status502BadGateway),
            };
        }
    }

    private static OrchestrationSessionDto ToDto(SessionResponse response) =>
        new(
            response.SessionId,
            response.Status,
            response.RequiresApproval,
            response.Goal,
            response.CostBlocked,
            string.IsNullOrEmpty(response.CostReason) ? null : response.CostReason,
            response.ReplanCount,
            response.Etag,
            response.PlanJson,
            response.Tasks.Select(task => new OrchestrationTaskDto(
                task.Id,
                task.Agent,
                task.Description,
                task.DependsOn.ToArray(),
                ParseInput(task.InputJson),
                task.Status,
                string.IsNullOrEmpty(task.Output) ? null : task.Output,
                string.IsNullOrEmpty(task.Error) ? null : task.Error)).ToArray());

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
}
