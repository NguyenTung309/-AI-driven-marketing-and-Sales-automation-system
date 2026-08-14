using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Api.Auth;
using Clawbot.Api.Common.Pagination;
using Clawbot.Domain.Jobs;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record AdminRecurringExecutionSummaryDto(
    Guid Id,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? FinishedAt);

public sealed record AdminRecurringJobDto(
    string Id,
    string Cron,
    string Queue,
    DateTime? NextExecution,
    DateTime? LastExecution,
    string? LastState,
    string? Agent,
    string? Description,
    bool CanTriggerManually,
    AdminRecurringExecutionSummaryDto? LatestExecution);

public sealed record AdminScheduleJobDto(
    Guid Id,
    string Name,
    string GoalTemplate,
    string Cadence,
    string TimezoneId,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastRunAt,
    bool IsActive,
    bool RequiresApproval,
    string? Agent,
    string Kind);

public sealed record AdminJobsResponse(
    IReadOnlyList<AdminRecurringJobDto> Recurring,
    IReadOnlyList<AdminScheduleJobDto> Schedules);

public sealed record RecurringExecutionAcceptedResponse(
    string DefinitionId,
    Guid TrackingId,
    string Status,
    string StatusUrl);

public sealed record AgentScheduleRunAcceptedResponse(
    Guid RunId,
    string Status,
    string StatusUrl,
    Guid? SessionId,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt);

public sealed record RecurringExecutionAttemptDto(
    Guid Id,
    int AttemptNumber,
    int RetryCount,
    string Status,
    string HangfireBackgroundJobId,
    string? WorkerId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error);

public sealed record RecurringExecutionAttemptCursorPage(
    IReadOnlyList<RecurringExecutionAttemptDto> Items,
    string? NextCursor,
    int? Total);

public sealed record RecurringExecutionCursorPage(
    IReadOnlyList<AdminRecurringExecutionSummaryDto> Items,
    string? NextCursor,
    int? Total);

// Admin overview of every automated job: Hangfire recurring jobs (system-wide, from Hangfire
// storage — the source of truth for what is actually registered) plus the tenant's
// AgentSchedules (OrchestrationV2). Read-only list + trigger/pause/activate/run-now actions.
public static class AdminJobsEndpoints
{
    private const string OrchestrationKind = "orchestration";
    private const string TrendScanKind = "trend-scan";

    // Hangfire job id -> (agent driven via gRPC, VN description). Agent empty = pure system job.
    private static readonly Dictionary<string, (string? Agent, string Description)> JobMeta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["retention-purge"] = (null, "Dọn dữ liệu hết hạn retention"),
            ["kpi-daily-rollup"] = (null, "Tổng hợp KPI theo ngày"),
            ["kpi-daily-rollup-intraday"] = (null, "Tổng hợp KPI trong ngày (mỗi giờ)"),
            ["refresh-token-cleanup"] = (null, "Dọn refresh token hết hạn"),
            ["daily-report-push"] = (null, "Đẩy báo cáo ngày cho tenant"),
            ["kpi-anomaly-alert"] = ("report-agent", "Cảnh báo bất thường KPI"),
            ["kpi-forecast-precompute"] = ("report-agent", "Tính trước dự báo KPI"),
            ["content-weekly-trend-scan"] = ("research-agent", "Quét xu hướng nội dung hằng tuần"),
            ["content-publish-due"] = (null, "Đăng bài đã lên lịch đến hạn"),
            ["health-check"] = (null, "Kiểm tra sức khoẻ hệ thống"),
            ["out-of-hours-auto-reply"] = (null, "Tự động trả lời ngoài giờ"),
            ["drip-sequence-sender"] = (null, "Gửi chuỗi tin nhắn drip"),
            ["idle-conversation-alert"] = (null, "Cảnh báo hội thoại bị bỏ quên"),
            ["lead-followup"] = (null, "Nhắc follow-up khách hàng tiềm năng"),
            ["kb-accuracy-check"] = (null, "Kiểm tra độ chính xác tri thức KB"),
            ["inbox-daily-summary"] = (null, "Tóm tắt inbox cuối ngày"),
            ["competitor-scan"] = (null, "Quét đối thủ cạnh tranh"),
        };

    public static IEndpointRouteBuilder MapAdminJobs(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin/jobs")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("", ListAsync).RequirePermission("system:config");
        grp.MapPost("/recurring/{id}/trigger", TriggerRecurringAsync).RequirePermission("system:config");
        grp.MapGet("/recurring/{id}/executions", GetRecurringExecutionsAsync).RequirePermission("system:config");
        grp.MapGet("/executions/{id:guid}", GetRecurringExecutionAsync).RequirePermission("system:config");
        grp.MapGet("/executions/{id:guid}/attempts", GetRecurringExecutionAttemptsAsync).RequirePermission("system:config");
        grp.MapPost("/executions/{id:guid}/retry", RetryRecurringExecutionAsync).RequirePermission("system:config");
        grp.MapGet("/schedule-runs/{id:guid}", GetScheduleRunAsync).RequirePermission("system:config");
        grp.MapPost("/schedules/{id:guid}/pause", PauseScheduleAsync).RequirePermission("system:config");
        grp.MapPost("/schedules/{id:guid}/activate", ActivateScheduleAsync).RequirePermission("system:config");
        grp.MapPost("/schedules/{id:guid}/run-now", RunScheduleNowAsync).RequirePermission("system:config");
        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var latestTrackedByDefinition = await db.RecurringJobExecutions.AsNoTracking()
            .Where(execution => execution.RequestedTenantId == tenant.TenantId)
            .GroupBy(execution => execution.DefinitionId)
            .Select(group => new
            {
                DefinitionId = group.Key,
                Execution = group
                    .OrderByDescending(execution => execution.RequestedAt)
                    .ThenByDescending(execution => execution.Id)
                    .Select(execution => new AdminRecurringExecutionSummaryDto(
                        execution.Id,
                        execution.Status,
                        execution.RequestedAt,
                        execution.FinishedAt))
                    .First(),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var latestTrackedByDefinitionId = latestTrackedByDefinition
            .ToDictionary(execution => execution.DefinitionId, execution => execution.Execution);

        using var connection = JobStorage.Current.GetConnection();
        var recurring = connection.GetRecurringJobs()
            .OrderBy(j => j.Id, StringComparer.OrdinalIgnoreCase)
            .Select(j =>
            {
                var meta = JobMeta.TryGetValue(j.Id, out var m) ? m : (Agent: null, Description: string.Empty);
                return new AdminRecurringJobDto(
                    j.Id,
                    j.Cron ?? string.Empty,
                    j.Queue ?? "default",
                    j.NextExecution,
                    j.LastExecution,
                    j.LastJobState,
                    meta.Agent,
                    string.IsNullOrEmpty(meta.Description) ? null : meta.Description,
                    CanTriggerManually: string.Equals(
                        j.Id,
                        RecurringJobDefinitions.HealthCheck,
                        StringComparison.Ordinal),
                    LatestExecution: latestTrackedByDefinitionId.GetValueOrDefault(j.Id));
            })
            .ToList();

        var schedules = (await db.AgentSchedules.AsNoTracking()
                .Where(s => s.DeletedAt == null)
                .ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => ToScheduleDto(s))
            .ToList();

        return Results.Ok(new AdminJobsResponse(recurring, schedules));
    }

    private static async Task<IResult> TriggerRecurringAsync(
        string id,
        RecurringJobExecutionService tracking,
        IBackgroundJobClient backgroundJobs,
        ITenantAccessor tenants,
        HttpContext http,
        CancellationToken ct)
    {
        if (!string.Equals(id?.Trim(), RecurringJobDefinitions.HealthCheck, StringComparison.Ordinal))
            return Results.NotFound(new { error = "recurring_job_definition_not_found" });

        if (!TryGetIdempotencyKey(http, out var requestKey, out var idempotencyKeyError))
            return Results.BadRequest(new { error = idempotencyKeyError });

        var tenant = tenants.Require();
        var requestedByUserId = CurrentInteractiveUserId(http);
        if (requestedByUserId is null)
            return Results.Forbid();

        RecurringJobExecution execution;
        try
        {
            execution = await tracking.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
                RecurringJobDefinitions.HealthCheck,
                requestedByUserId.Value,
                tenant.TenantId,
                requestKey), ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message, "recurring_execution_request_key_conflict", StringComparison.Ordinal))
        {
            return Results.Conflict(new { error = "idempotency_key_conflict" });
        }

        return await EnqueueManualExecutionAsync(execution, tracking, backgroundJobs, ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetRecurringExecutionsAsync(
        string id,
        string? cursor,
        int pageSize,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var definitionId = id.Trim();
        if (!JobMeta.ContainsKey(definitionId))
            return Results.NotFound(new { error = "recurring_job_definition_not_found" });

        var tenant = tenants.Require();
        pageSize = KeysetQuery.ClampPageSize(pageSize);
        var cursorKey = KeysetQuery.Decode(cursor);
        var query = db.RecurringJobExecutions.AsNoTracking()
            .Where(execution => execution.DefinitionId == definitionId
                && execution.RequestedTenantId == tenant.TenantId);
        var total = cursorKey is null ? await query.CountAsync(ct).ConfigureAwait(false) : (int?)null;
        if (cursorKey is { } key)
        {
            query = query.Where(execution => execution.RequestedAt < key.Ts
                || (execution.RequestedAt == key.Ts && execution.Id.CompareTo(key.Id) < 0));
        }

        var fetched = await query
            .OrderByDescending(execution => execution.RequestedAt)
            .ThenByDescending(execution => execution.Id)
            .Select(execution => new AdminRecurringExecutionSummaryDto(
                execution.Id,
                execution.Status,
                execution.RequestedAt,
                execution.FinishedAt))
            .Take(pageSize + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var (items, nextCursor) = KeysetQuery.SliceWithCursor(
            fetched,
            pageSize,
            execution => execution.RequestedAt,
            execution => execution.Id);

        return Results.Ok(new RecurringExecutionCursorPage(items, nextCursor, total));
    }

    private static async Task<IResult> RetryRecurringExecutionAsync(
        Guid id,
        AppDbContext db,
        RecurringJobExecutionService tracking,
        IBackgroundJobClient backgroundJobs,
        ITenantAccessor tenants,
        HttpContext http,
        CancellationToken ct)
    {
        if (!TryGetIdempotencyKey(http, out var requestKey, out var idempotencyKeyError))
            return Results.BadRequest(new { error = idempotencyKeyError });

        var tenant = tenants.Require();
        var requestedByUserId = CurrentInteractiveUserId(http);
        if (requestedByUserId is null)
            return Results.Forbid();

        var original = await db.RecurringJobExecutions.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id
                && (candidate.RequestedTenantId == tenant.TenantId), ct)
            .ConfigureAwait(false);
        if (original is null
            || !string.Equals(original.DefinitionId, RecurringJobDefinitions.HealthCheck, StringComparison.Ordinal))
        {
            return Results.NotFound(new { error = "recurring_execution_not_found" });
        }

        if (original.Status is not (RecurringJobExecutionStatuses.Failed
            or RecurringJobExecutionStatuses.EnqueueFailed))
        {
            return Results.Conflict(new { error = "recurring_execution_not_retryable" });
        }

        RecurringJobExecution retry;
        try
        {
            retry = await tracking.CreateManualRetryAsync(id, new RecurringJobExecutionRequest(
                original.DefinitionId,
                requestedByUserId.Value,
                tenant.TenantId,
                requestKey), ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message, "recurring_execution_request_key_conflict", StringComparison.Ordinal))
        {
            return Results.Conflict(new { error = "idempotency_key_conflict" });
        }
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message, "recurring_execution_not_terminal", StringComparison.Ordinal))
        {
            return Results.Conflict(new { error = "recurring_execution_not_terminal" });
        }

        return await EnqueueManualExecutionAsync(retry, tracking, backgroundJobs, ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetRecurringExecutionAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var execution = await db.RecurringJobExecutions.AsNoTracking()
            .Where(candidate => candidate.Id == id
                && (candidate.RequestedTenantId == tenant.TenantId))
            .Select(candidate => new
            {
                candidate.Id,
                candidate.DefinitionId,
                candidate.Source,
                candidate.RetryOfExecutionId,
                candidate.Status,
                candidate.RequestedAt,
                candidate.EnqueuedAt,
                candidate.StartedAt,
                candidate.FinishedAt,
                ProgressPercent = candidate.ProgressPercent ?? 0,
                candidate.ProgressNote,
                candidate.ResultSummary,
                candidate.ResultLink,
                candidate.Error,
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return execution is null
            ? Results.NotFound(new { error = "recurring_execution_not_found" })
            : Results.Ok(execution);
    }

    private static async Task<IResult> GetRecurringExecutionAttemptsAsync(
        Guid id,
        string? cursor,
        int pageSize,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        pageSize = KeysetQuery.ClampPageSize(pageSize);
        var cursorKey = KeysetQuery.Decode(cursor);
        var executionExists = await db.RecurringJobExecutions
            .AsNoTracking()
            .AnyAsync(execution => execution.Id == id
                && execution.RequestedTenantId == tenant.TenantId, ct)
            .ConfigureAwait(false);
        if (!executionExists)
            return Results.NotFound(new { error = "recurring_execution_not_found" });

        var query = db.RecurringJobExecutionAttempts
            .AsNoTracking()
            .Where(attempt => attempt.ExecutionId == id);
        var total = cursorKey is null ? await query.CountAsync(ct).ConfigureAwait(false) : (int?)null;
        if (cursorKey is { } key)
        {
            query = query.Where(attempt => attempt.StartedAt < key.Ts
                || (attempt.StartedAt == key.Ts && attempt.Id.CompareTo(key.Id) < 0));
        }

        var fetched = await query
            .OrderByDescending(attempt => attempt.StartedAt)
            .ThenByDescending(attempt => attempt.Id)
            .Select(attempt => new RecurringExecutionAttemptDto(
                attempt.Id,
                attempt.AttemptNumber,
                attempt.RetryCount,
                attempt.Status,
                attempt.HangfireBackgroundJobId,
                attempt.WorkerId,
                attempt.StartedAt,
                attempt.FinishedAt,
                attempt.Error))
            .Take(pageSize + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var (items, nextCursor) = KeysetQuery.SliceWithCursor(
            fetched,
            pageSize,
            attempt => attempt.StartedAt,
            attempt => attempt.Id);

        return Results.Ok(new RecurringExecutionAttemptCursorPage(items, nextCursor, total));
    }

    private static async Task<IResult> GetScheduleRunAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var run = await db.AgentScheduleRuns.AsNoTracking()
            .Where(candidate => candidate.Id == id && candidate.TenantId == tenant.TenantId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.ScheduleId,
                candidate.SessionId,
                candidate.Status,
                Error = candidate.Error == null ? null : "Tác vụ thực thi không thành công.",
                candidate.StartedAt,
                candidate.LastHeartbeatAt,
                candidate.FinishedAt,
            })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return run is null
            ? Results.NotFound(new { error = "schedule_run_not_found" })
            : Results.Ok(run);
    }

    private static bool TryGetIdempotencyKey(
        HttpContext http,
        out string requestKey,
        out string error)
    {
        requestKey = http.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (string.IsNullOrEmpty(requestKey))
        {
            error = "idempotency_key_required";
            return false;
        }

        if (!Guid.TryParse(requestKey, out var parsed) || parsed == Guid.Empty)
        {
            error = "idempotency_key_invalid";
            return false;
        }

        requestKey = parsed.ToString("D");
        error = string.Empty;
        return true;
    }

    private static string GetRecurringExecutionStatusUrl(Guid executionId) =>
        $"/api/admin/jobs/executions/{executionId:D}";

    private static async Task<IResult> EnqueueManualExecutionAsync(
        RecurringJobExecution execution,
        RecurringJobExecutionService tracking,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        if (RecurringJobExecutionStatuses.IsTerminal(execution.Status)
            || execution.HangfireBackgroundJobId is not null)
        {
            return Results.Ok(ToRecurringExecutionResponse(execution));
        }

        var claim = await tracking.ClaimEnqueueAsync(execution.Id, ct).ConfigureAwait(false);
        if (claim is null)
        {
            // Another request may have handed the workload to Hangfire already. Do not enqueue a
            // second delivery when that external side effect cannot be disproved safely.
            return AcceptedRecurringExecution(execution);
        }

        string hangfireJobId;
        try
        {
            hangfireJobId = backgroundJobs.Enqueue<HealthCheckRecurringJob>(
                job => job.RunManualAsync(execution.Id, null!, CancellationToken.None));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await MarkUnenqueuedExecutionFailedAsync(execution, tracking, claim).ConfigureAwait(false);
        }

        try
        {
            await tracking.AttachEnqueueAsync(claim, hangfireJobId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hangfire accepted this workload. The dispatcher uses PerformContext.BackgroundJob.Id
            // to repair this missing correlation on its first delivery, so the request remains
            // nonterminal and must not be enqueued again.
            return AcceptedRecurringExecution(execution, RecurringJobExecutionStatuses.Requested);
        }

        return AcceptedRecurringExecution(execution);
    }

    private static async Task<IResult> MarkUnenqueuedExecutionFailedAsync(
        RecurringJobExecution execution,
        RecurringJobExecutionService tracking,
        RecurringJobExecutionEnqueueClaim claim)
    {
        try
        {
            await tracking.ReleaseEnqueueClaimAsync(claim, CancellationToken.None).ConfigureAwait(false);
            await tracking.MarkEnqueueFailedAsync(
                claim.ExecutionId,
                "Không thể xác nhận yêu cầu đã được xếp hàng.",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(
                ToRecurringExecutionResponse(execution, RecurringJobExecutionStatuses.Requested),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(
            ToRecurringExecutionResponse(execution),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult AcceptedRecurringExecution(
        RecurringJobExecution execution,
        string? status = null)
    {
        var response = ToRecurringExecutionResponse(execution, status);
        return Results.Accepted(response.StatusUrl, response);
    }

    private static RecurringExecutionAcceptedResponse ToRecurringExecutionResponse(
        RecurringJobExecution execution,
        string? status = null) => new(
            execution.DefinitionId,
            execution.Id,
            status ?? execution.Status,
            GetRecurringExecutionStatusUrl(execution.Id));

    private static Guid? CurrentInteractiveUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)
        && userId != Guid.Empty
        && Guid.TryParse(http.User.FindFirst("role_id")?.Value, out var roleId)
        && roleId != Guid.Empty
            ? userId
            : null;

    private static async Task<IResult> PauseScheduleAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        _ = tenants.Require();
        var schedule = await db.AgentSchedules.FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null)
            return Results.NotFound(new { error = "schedule_not_found" });

        schedule.Pause(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToScheduleDto(schedule));
    }

    private static async Task<IResult> ActivateScheduleAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        _ = tenants.Require();
        var schedule = await db.AgentSchedules.FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null)
            return Results.NotFound(new { error = "schedule_not_found" });

        schedule.Activate(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToScheduleDto(schedule));
    }

    private static async Task<IResult> RunScheduleNowAsync(
        Guid id,
        ITenantAccessor tenants,
        Orchestrator.OrchestratorClient grpc,
        HttpContext http,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var requestedByUserId = CurrentInteractiveUserId(http);
        if (requestedByUserId is null)
            return Results.Forbid();

        try
        {
            var response = await grpc.RunScheduleAsync(new RunScheduleRequest
            {
                TenantId = tenant.TenantId.ToString("D"),
                ScheduleId = id.ToString("D"),
                UserId = requestedByUserId.Value.ToString("D"),
            }, cancellationToken: ct).ConfigureAwait(false);

            return response.Status switch
            {
                "started" when Guid.TryParse(response.RunId, out var runId) && runId != Guid.Empty => Results.Accepted(
                    $"/api/admin/jobs/schedule-runs/{runId:D}",
                    new AgentScheduleRunAcceptedResponse(
                        runId,
                        response.Status,
                        $"/api/admin/jobs/schedule-runs/{runId:D}",
                        Guid.TryParse(response.SessionId, out var sessionId) && sessionId != Guid.Empty ? sessionId : null,
                        response.NextRunAt?.ToDateTimeOffset(),
                        response.LastRunAt?.ToDateTimeOffset())),
                "started" => Results.Problem("schedule_run_tracking_id_missing", statusCode: StatusCodes.Status502BadGateway),
                "skipped_overlap" => Results.Conflict(new
                {
                    error = "schedule_run_in_progress",
                    message = "Lịch đang có phiên chạy chưa xong — chờ phiên đó kết thúc rồi thử lại.",
                }),
                "not_found" => Results.NotFound(new { error = "schedule_not_found" }),
                _ => Results.Problem("unexpected_schedule_run_status", statusCode: StatusCodes.Status502BadGateway),
            };
        }
        catch (RpcException ex)
        {
            return ToScheduleRunGrpcResult(ex);
        }
    }

    private static IResult ToScheduleRunGrpcResult(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.NotFound => Results.NotFound(new { error = "schedule_not_found" }),
        StatusCode.InvalidArgument => Results.BadRequest(new { error = "invalid_schedule_run_request" }),
        StatusCode.FailedPrecondition => Results.Conflict(new { error = "schedule_run_precondition_failed" }),
        StatusCode.Unauthenticated => Results.Unauthorized(),
        StatusCode.PermissionDenied => Results.Forbid(),
        StatusCode.ResourceExhausted => Results.Json(new { error = "schedule_run_rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests),
        StatusCode.Unavailable => Results.Json(new { error = "schedule_service_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable),
        StatusCode.DeadlineExceeded => Results.Json(new { error = "schedule_run_timeout" }, statusCode: StatusCodes.Status504GatewayTimeout),
        _ => Results.Problem("Không thể bắt đầu chạy lịch.", statusCode: StatusCodes.Status502BadGateway),
    };

    private static AdminScheduleJobDto ToScheduleDto(Clawbot.Domain.Agents.AgentSchedule s)
    {
        var isTrendScan = string.Equals(s.GoalTemplate, ContentTrendSettings.ScheduleGoalMarker, StringComparison.OrdinalIgnoreCase);
        return new AdminScheduleJobDto(
            s.Id,
            s.Name,
            s.GoalTemplate,
            s.Cadence,
            s.TimezoneId,
            s.NextRunAt,
            s.LastRunAt,
            s.IsActive,
            s.RequiresApproval,
            isTrendScan ? "research-agent" : null,
            isTrendScan ? TrendScanKind : OrchestrationKind);
    }
}
