using System.Data;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.AgentService.Services;

public sealed partial class AgentScheduleRunner(
    AppDbContext db,
    IAgentScheduleLeaseProvider leaseProvider,
    IAutonomousOrchestrator orchestrator,
    ITenantTrendScanner trendScanner,
    IAutonomousRunSink runSink,
    IOrchestratorPermissionResolver permissionResolver,
    IClock clock,
    IServiceScopeFactory scopeFactory,
    ILogger<AgentScheduleRunner> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IAgentScheduleLeaseProvider _leaseProvider = leaseProvider;
    private readonly IAutonomousOrchestrator _orchestrator = orchestrator;
    private readonly ITenantTrendScanner _trendScanner = trendScanner;
    private readonly IAutonomousRunSink _runSink = runSink;
    private readonly IOrchestratorPermissionResolver _permissionResolver = permissionResolver;
    private readonly IClock _clock = clock;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<AgentScheduleRunner> _logger = logger;

    public sealed record ManualRunResult(
        string Status,
        Guid? SessionId = null,
        DateTimeOffset? NextRunAt = null,
        DateTimeOffset? LastRunAt = null);

    public async Task<AgentScheduleRun?> RunDueAsync(Guid scheduleId, DateTimeOffset dueAtUtc, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        var schedule = await _db.AgentSchedules
            .FromSqlInterpolated($"SELECT * FROM dbo.agent_schedules WITH (UPDLOCK, HOLDLOCK) WHERE id = {scheduleId} AND deleted_at IS NULL")
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (schedule is null || !schedule.IsActive || schedule.NextRunAt != dueAtUtc)
            return null;

        // C2: event-triggered schedules — mỗi lần dispatcher kéo NextRunAt về now là một window riêng
        // (ticks-based), và sau khi chạy thì ngủ tới sự kiện kế tiếp thay vì lặp theo cadence.
        var isEventTriggered = IsEventTriggered(schedule);
        var windowKey = isEventTriggered
            ? $"event:{dueAtUtc.UtcTicks}"
            : RecurrenceCalculator.WindowKey(schedule.Cadence, dueAtUtc, schedule.TimezoneId);
        var now = _clock.UtcNow;
        var nextRunAt = NextRunAt(schedule, dueAtUtc, now);
        var existing = await _db.AgentScheduleRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ScheduleId == schedule.Id && r.WindowKey == windowKey, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            // Trước đây return ngay tại đây làm NextRunAt mắc kẹt ở mốc quá khứ mãi mãi. Không ghi
            // LastRunAt vì window này đã chạy hoặc đã bị skip, chỉ bỏ qua mốc cũ theo misfire policy.
            if (schedule.NextRunAt <= dueAtUtc)
            {
                schedule.Reschedule(nextRunAt, now);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return existing;
        }

        var run = AgentScheduleRun.Start(
            schedule.TenantId,
            schedule.Id,
            windowKey,
            now,
            schedule.InitiatorUserId);
        if (await HasStartedRunAsync(schedule.Id, ct).ConfigureAwait(false))
        {
            run.SkipOverlap(now);
            _db.AgentScheduleRuns.Add(run);
            // Overlap bị skip không phải một lần đã chạy, nên không được ghi đè LastRunAt.
            schedule.Reschedule(nextRunAt, now);
            var overlapDuplicate = await SaveOrGetDuplicateAsync(schedule, run, null, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return overlapDuplicate ?? run;
        }

        if (IsTrendScan(schedule))
        {
            _db.AgentScheduleRuns.Add(run);
            schedule.RecordRun(now, nextRunAt, now);
            var trendDuplicate = await SaveOrGetDuplicateAsync(schedule, run, null, ct).ConfigureAwait(false);
            if (trendDuplicate is not null)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return trendDuplicate;
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            QueueContinuation(run.Id);
            return run;
        }

        var session = AgentSession.CreatePlan(
            schedule.TenantId,
            schedule.GoalTemplate,
            "{}",
            schedule.RequiresApproval,
            now,
            schedule.InitiatorUserId);
        run.LinkSession(session.Id);
        _db.AgentSessions.Add(session);
        _db.AgentScheduleRuns.Add(run);
        schedule.RecordRun(now, nextRunAt, now);
        var duplicate = await SaveOrGetDuplicateAsync(schedule, run, session, ct).ConfigureAwait(false);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return duplicate;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        QueueContinuation(run.Id);
        return run;
    }

    public async Task<ManualRunResult> RunNowAsync(
        Guid tenantId,
        Guid scheduleId,
        Guid? userId,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        var schedule = await _db.AgentSchedules
            .FromSqlInterpolated($"SELECT * FROM dbo.agent_schedules WITH (UPDLOCK, HOLDLOCK) WHERE id = {scheduleId} AND tenant_id = {tenantId} AND deleted_at IS NULL")
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (schedule is null)
            return new ManualRunResult("not_found");

        if (await HasStartedRunAsync(schedule.Id, ct).ConfigureAwait(false))
            return new ManualRunResult("skipped_overlap");

        var now = _clock.UtcNow;
        // A manual attempt must not turn its click time into the recurring cadence anchor.
        var nextRunAt = NextRunAt(schedule, schedule.NextRunAt, now);
        // Guid là phần idempotency của manual window, tránh hai thao tác cùng tick dính unique index.
        var run = AgentScheduleRun.Start(
            schedule.TenantId,
            schedule.Id,
            $"manual:{now.UtcTicks}:{Guid.NewGuid():N}",
            now,
            userId);
        AgentSession? session = null;
        if (!IsTrendScan(schedule))
        {
            session = AgentSession.CreatePlan(schedule.TenantId, schedule.GoalTemplate, "{}", schedule.RequiresApproval, now, userId);
            run.LinkSession(session.Id);
            _db.AgentSessions.Add(session);
        }

        _db.AgentScheduleRuns.Add(run);
        // Ghi nhận ngay khi bắt đầu thử chạy. Kết quả failed/cancelled không được làm mất mốc này.
        schedule.RecordRun(now, nextRunAt, now);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            _db.Entry(run).State = EntityState.Detached;
            _db.Entry(schedule).State = EntityState.Detached;
            if (session is not null)
                _db.Entry(session).State = EntityState.Detached;

            if (await HasStartedRunAsync(scheduleId, ct).ConfigureAwait(false))
                return new ManualRunResult("skipped_overlap");
            throw;
        }

        QueueContinuation(run.Id);
        return new ManualRunResult("started", session?.Id, nextRunAt, now);
    }

    private void QueueContinuation(Guid runId)
    {
        _ = Task.Run(() => ContinueInNewScopeAsync(runId), CancellationToken.None);
    }

    private async Task ContinueInNewScopeAsync(Guid runId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<AgentScheduleRunner>();
            await runner.ContinueRunAsync(runId).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The shared executor persists cancellation before rethrowing.
        }
        catch (Exception exception)
        {
            LogQueuedRunFailed(_logger, exception, runId);
            await FailQueuedRunAsync(runId).ConfigureAwait(false);
        }
    }

    internal async Task ContinueRunAsync(Guid runId)
    {
        var run = await _db.AgentScheduleRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == runId && r.Status == "started" && r.FinishedAt == null, CancellationToken.None)
            .ConfigureAwait(false);
        if (run is null)
            return;

        await using var lease = await _leaseProvider.TryAcquireAsync(_db, run.ScheduleId, CancellationToken.None).ConfigureAwait(false);
        if (lease is null)
            return;

        // The stale reaper may have terminalized this entity between its first read and lease
        // acquisition. Reload while the lease is held before any side-effecting continuation work.
        await _db.Entry(run).ReloadAsync(CancellationToken.None).ConfigureAwait(false);
        if (run.Status != "started" || run.FinishedAt is not null)
            return;

        var schedule = await _db.AgentSchedules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == run.ScheduleId && s.DeletedAt == null, CancellationToken.None)
            .ConfigureAwait(false);
        if (schedule is null)
        {
            run.Fail("schedule_not_found", _clock.UtcNow);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        using var executionCancellation = new CancellationTokenSource();
        using var heartbeatCancellation = new CancellationTokenSource();
        var heartbeatTask = KeepRunAliveAsync(run.Id, executionCancellation, heartbeatCancellation.Token);
        try
        {
            if (IsTrendScan(schedule))
            {
                if (await ResolveScheduleInitiatorPermissionsAsync(run, session: null, executionCancellation.Token)
                    .ConfigureAwait(false) is null)
                {
                    return;
                }

                await ExecuteTrendScanAsync(schedule.TenantId, run, executionCancellation.Token).ConfigureAwait(false);
                return;
            }

            if (run.SessionId is not { } sessionId)
            {
                run.Fail("schedule_session_not_found", _clock.UtcNow);
                await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var session = await _db.AgentSessions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == run.TenantId, CancellationToken.None)
                .ConfigureAwait(false);
            if (session is null)
            {
                run.Fail("schedule_session_not_found", _clock.UtcNow);
                await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var initiatorPermissions = await ResolveScheduleInitiatorPermissionsAsync(
                    run,
                    session,
                    executionCancellation.Token)
                .ConfigureAwait(false);
            if (initiatorPermissions is null)
                return;
            if (session.UserId != run.InitiatorUserId)
            {
                await FailForInvalidInitiatorAsync(run, session, "schedule_initiator_mismatch").ConfigureAwait(false);
                return;
            }

            var source = run.WindowKey.StartsWith("manual:", StringComparison.Ordinal) ? "manual" : "schedule";
            await ExecuteOrchestrationAsync(
                    schedule,
                    run,
                    session,
                    source,
                    initiatorPermissions,
                    executionCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation has already moved the run to a terminal state in the shared executor.
        }
        finally
        {
            heartbeatCancellation.Cancel();
            await heartbeatTask.ConfigureAwait(false);
        }
    }

    private async Task KeepRunAliveAsync(Guid runId, CancellationTokenSource executionCancellation, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<AgentScheduleRunner>();
                    if (!await runner.HeartbeatRunAsync(runId, ct).ConfigureAwait(false))
                    {
                        executionCancellation.Cancel();
                        return;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogHeartbeatFailed(_logger, exception, runId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The owning continuation has reached a terminal state.
        }
    }

    private async Task<bool> HeartbeatRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await _db.AgentScheduleRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == runId && r.Status == "started" && r.FinishedAt == null, ct)
            .ConfigureAwait(false);
        if (run is null)
            return false;

        run.Heartbeat(_clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task FailQueuedRunAsync(Guid runId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<AgentScheduleRunner>();
            await runner.FailQueuedRunAsyncInScope(runId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogQueuedRunFailurePersistenceFailed(_logger, exception, runId);
        }
    }

    private async Task FailQueuedRunAsyncInScope(Guid runId)
    {
        var run = await _db.AgentScheduleRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == runId && r.Status == "started" && r.FinishedAt == null, CancellationToken.None)
            .ConfigureAwait(false);
        if (run is null)
            return;

        var now = _clock.UtcNow;
        run.Fail("schedule_run_dispatch_failed", now);
        if (run.SessionId is { } sessionId)
        {
            var session = await _db.AgentSessions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == run.TenantId, CancellationToken.None)
                .ConfigureAwait(false);
            if (session?.Status is AgentSessionStatuses.Running
                or AgentSessionStatuses.PendingApproval
                or AgentSessionStatuses.PauseRequested
                or AgentSessionStatuses.Paused)
            {
                session.Fail(now);
            }
        }

        await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<AgentScheduleRun?> SaveOrGetDuplicateAsync(
        AgentSchedule schedule,
        AgentScheduleRun run,
        AgentSession? session,
        CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            _db.Entry(run).State = EntityState.Detached;
            _db.Entry(schedule).State = EntityState.Detached;
            if (session is not null)
                _db.Entry(session).State = EntityState.Detached;
            var duplicate = await _db.AgentScheduleRuns.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.ScheduleId == run.ScheduleId && r.WindowKey == run.WindowKey, ct)
                .ConfigureAwait(false);
            if (duplicate is not null)
                return duplicate;

            var overlappingRun = await _db.AgentScheduleRuns.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.ScheduleId == run.ScheduleId && r.Status == "started" && r.FinishedAt == null, ct)
                .ConfigureAwait(false);
            if (overlappingRun is not null)
                return overlappingRun;
            throw;
        }
    }

    private async Task FailForInvalidInitiatorAsync(
        AgentScheduleRun run,
        AgentSession? session,
        string reason)
    {
        var now = _clock.UtcNow;
        if (run.Status == "started" && run.FinishedAt is null)
            run.Fail(reason, now);
        if (session?.Status is AgentSessionStatuses.Running
            or AgentSessionStatuses.PendingApproval
            or AgentSessionStatuses.PauseRequested
            or AgentSessionStatuses.Paused)
        {
            session.Fail(now);
        }

        await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<IReadOnlySet<string>?> ResolveScheduleInitiatorPermissionsAsync(
        AgentScheduleRun run,
        AgentSession? session,
        CancellationToken ct)
    {
        if (run.InitiatorUserId is not { } initiatorUserId)
        {
            await FailForInvalidInitiatorAsync(run, session, "schedule_initiator_missing").ConfigureAwait(false);
            return null;
        }

        IReadOnlySet<string> initiatorPermissions;
        try
        {
            initiatorPermissions = await _permissionResolver.ResolvePermissionsAsync(
                    run.TenantId,
                    initiatorUserId,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Grpc.Core.RpcException)
        {
            await FailForInvalidInitiatorAsync(run, session, "schedule_initiator_invalid").ConfigureAwait(false);
            return null;
        }

        if (!initiatorPermissions.Contains("orchestration:run"))
        {
            await FailForInvalidInitiatorAsync(run, session, "schedule_initiator_permission_denied").ConfigureAwait(false);
            return null;
        }

        return initiatorPermissions;
    }

    private async Task ExecuteOrchestrationAsync(
        AgentSchedule schedule,
        AgentScheduleRun run,
        AgentSession session,
        string source,
        IReadOnlySet<string> initiatorPermissions,
        CancellationToken ct)
    {
        AutonomousRunResult result;
        try
        {
            result = await _orchestrator.RunAsync(
                new AutonomousRunRequest(
                    schedule.TenantId,
                    session.Id,
                    session.Goal ?? schedule.GoalTemplate,
                    source,
                    session.RequiresApproval,
                    ExecutionPermissions: initiatorPermissions),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A stale-run reaper can have terminalized the row while the LLM call was unwinding.
            // Reload so this owner never overwrites a reaped terminal state with cancelled.
            await _db.Entry(run).ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            await _db.Entry(session).ReloadAsync(CancellationToken.None).ConfigureAwait(false);

            var now = _clock.UtcNow;
            if (session.Status is AgentSessionStatuses.Running or AgentSessionStatuses.PauseRequested or AgentSessionStatuses.Paused)
            {
                try
                {
                    await _runSink.CancelAsync(
                        session.TenantId,
                        session.Id,
                        session.ReplanCount,
                        session.RowVersion,
                        now,
                        CancellationToken.None).ConfigureAwait(false);
                    await _db.Entry(session).ReloadAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (OrchestrationPlanGenerationMismatchException)
                {
                    // A replacement plan owns the session now; this cancelled continuation must not stop it.
                }
                catch (OrchestrationSessionEtagMismatchException)
                {
                    // A newer session mutation won after this stale runner read its ETag.
                }
            }
            if (run.Status == "started" && run.FinishedAt is null)
                run.Cancel(now);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            var now = _clock.UtcNow;
            if (run.Status == "started" && run.FinishedAt is null)
            {
                run.Fail("orchestrator_run_failed", now);
                // Persist the scheduler outcome separately. The session failure and its content cleanup use the
                // sink's transaction below; an old runner may not alter a newer plan if generation changed.
                await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            try
            {
                await _runSink.FailAndRejectOrphanedContentAsync(
                    session.TenantId,
                    session.Id,
                    "orchestrator_run_failed",
                    session.ReplanCount,
                    now,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OrchestrationPlanGenerationMismatchException)
            {
                // A newer plan owns this session now; do not fail it from the stale scheduler continuation.
            }
            catch (OrchestrationSessionNotRunningException)
            {
                // The run was already stopped or terminalized by another actor.
            }

            throw;
        }

        if (result.Status is "completed" or "pending_approval")
            run.Complete(_clock.UtcNow);
        else if (result.Status == "cancelled")
            run.Cancel(_clock.UtcNow);
        else
            run.Fail(result.Reason ?? result.Status, _clock.UtcNow);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static DateTimeOffset NextRunAt(AgentSchedule schedule, DateTimeOffset fromUtc, DateTimeOffset now)
    {
        if (IsEventTriggered(schedule))
            return DateTimeOffset.MaxValue;

        // Start from the scheduled occurrence (not worker poll time) so daily/weekly/monthly cadence
        // remains anchored. Skip all missed occurrences to the first future one.
        var nextRunAt = RecurrenceCalculator.NextRunUtc(schedule.Cadence, fromUtc, schedule.TimezoneId);
        while (nextRunAt <= now)
            nextRunAt = RecurrenceCalculator.NextRunUtc(schedule.Cadence, nextRunAt, schedule.TimezoneId);
        return nextRunAt;
    }

    private static bool IsEventTriggered(AgentSchedule schedule) =>
        string.Equals(schedule.TriggerType, "event", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrendScan(AgentSchedule schedule) =>
        string.Equals(schedule.GoalTemplate, ContentTrendSettings.ScheduleGoalMarker, StringComparison.OrdinalIgnoreCase);

    // Direct trend-scan path: no AgentSession and no LLM round — the scanner persists the briefs itself.
    private async Task ExecuteTrendScanAsync(Guid tenantId, AgentScheduleRun run, CancellationToken ct)
    {
        try
        {
            await _trendScanner.ScanAndPersistAsync(
                tenantId,
                ContentTrendBriefFormatter.CurrentWeekOf(_clock.UtcNow),
                ct).ConfigureAwait(false);
            run.Complete(_clock.UtcNow);
        }
        catch (OperationCanceledException)
        {
            await _db.Entry(run).ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            if (run.Status == "started" && run.FinishedAt is null)
                run.Cancel(_clock.UtcNow);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            // A run left in "started" would block every future window via the overlap check.
            LogTrendScanFailed(_logger, ex, run.Id);
            run.Fail("trend_scan_failed", _clock.UtcNow);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private async Task<bool> HasStartedRunAsync(Guid scheduleId, CancellationToken ct) =>
        await _db.AgentScheduleRuns.IgnoreQueryFilters()
            .AnyAsync(r => r.ScheduleId == scheduleId && r.Status == "started" && r.FinishedAt == null, ct)
            .ConfigureAwait(false);

    [LoggerMessage(EventId = 1122, Level = LogLevel.Error, Message = "Queued agent schedule run failed for run {RunId}")]
    private static partial void LogQueuedRunFailed(ILogger logger, Exception exception, Guid runId);

    [LoggerMessage(EventId = 1125, Level = LogLevel.Error, Message = "Could not persist terminal failure for queued agent schedule run {RunId}")]
    private static partial void LogQueuedRunFailurePersistenceFailed(ILogger logger, Exception exception, Guid runId);

    [LoggerMessage(EventId = 1126, Level = LogLevel.Warning, Message = "Could not renew heartbeat for agent schedule run {RunId}")]
    private static partial void LogHeartbeatFailed(ILogger logger, Exception exception, Guid runId);

    [LoggerMessage(EventId = 1123, Level = LogLevel.Error, Message = "Trend scan schedule run failed for run {RunId}")]
    private static partial void LogTrendScanFailed(ILogger logger, Exception exception, Guid runId);
}
