using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed partial class AgentScheduleWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<AgentScheduleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleRunAfter = TimeSpan.FromHours(2);
    private static readonly TimeSpan StaleSessionAfter = TimeSpan.FromHours(6);
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        await RunTickAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await RunTickAsync(stoppingToken).ConfigureAwait(false);
    }

    // Câu truy vấn liệt kê lịch nằm NGOÀI try/catch của từng lịch: SQL Server chớp tắt (container
    // restart) là nó ném thẳng ra khỏi ExecuteAsync, và mặc định BackgroundServiceExceptionBehavior
    // = StopHost giết cả host — agent service chết hẳn tới khi có người khởi động lại tay.
    // Bọc từng nhịp lại để lỗi tạm thời chỉ mất một nhịp, nhịp sau chạy tiếp.
    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessDueAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogTickFailed(logger, exception);
        }
    }

    internal async Task ProcessDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        List<DueSchedule> due;
        using (var enumerationScope = scopeFactory.CreateScope())
        {
            var db = enumerationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var runSink = enumerationScope.ServiceProvider.GetRequiredService<IAutonomousRunSink>();
            await ReapStaleRunsAsync(db, runSink, now, ct).ConfigureAwait(false);
            due = await db.AgentSchedules.IgnoreQueryFilters()
                .Where(s => s.IsActive && s.DeletedAt == null && s.NextRunAt <= now)
                .OrderBy(s => s.NextRunAt)
                .Take(BatchSize)
                .Select(s => new DueSchedule(s.Id, s.NextRunAt))
                .ToListAsync(ct).ConfigureAwait(false);
        }

        foreach (var item in due)
        {
            // Mỗi lịch một scope, tức một AppDbContext riêng. Dùng chung context cho cả lô thì một
            // entity hỏng (ví dụ lead có contact_id không tồn tại) nằm lại ở trạng thái Added và bị
            // flush lại ở mọi SaveChanges sau đó — một lịch lỗi kéo sập toàn bộ lô, và trace ghi
            // trong cùng batch cũng mất theo nên không còn dấu vết để lần.
            using var runScope = scopeFactory.CreateScope();
            var runner = runScope.ServiceProvider.GetRequiredService<AgentScheduleRunner>();
            try
            {
                await runner.RunDueAsync(item.Id, item.NextRunAt, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogScheduleRunFailed(logger, ex, item.Id);
            }
        }
    }

    // A process can die after persisting a started row but before terminal status. Reap only rows that
    // have exceeded a deliberately long threshold; otherwise overlap=skip would disable a schedule forever.
    private async Task ReapStaleRunsAsync(
        AppDbContext db,
        IAutonomousRunSink runSink,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var staleRuns = await db.AgentScheduleRuns.IgnoreQueryFilters()
            .Where(r => r.Status == "started"
                && r.FinishedAt == null
                && (r.LastHeartbeatAt ?? r.StartedAt) < now - StaleRunAfter)
            .Select(r => new { r.Id, r.ScheduleId, r.TenantId, r.SessionId })
            .ToListAsync(ct).ConfigureAwait(false);
        var reapedRunCount = 0;
        var staleCutoff = now - StaleRunAfter;
        foreach (var staleRun in staleRuns)
        {
            await using var lease = await AgentScheduleLease.TryAcquireAsync(db, staleRun.ScheduleId, ct).ConfigureAwait(false);
            if (lease is null)
                continue;

            // Recheck the predicate under the lease: a continuation may have renewed after the
            // snapshot query, and a live owner must never be reaped or have overlap released.
            var reaped = await db.AgentScheduleRuns.IgnoreQueryFilters()
                .Where(r => r.Id == staleRun.Id
                    && r.Status == "started"
                    && r.FinishedAt == null
                    && (r.LastHeartbeatAt ?? r.StartedAt) < staleCutoff)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, "failed")
                    .SetProperty(r => r.Error, "stale_run_reaped")
                    .SetProperty(r => r.FinishedAt, now), ct)
                .ConfigureAwait(false);
            reapedRunCount += reaped;
            if (reaped == 0 || staleRun.SessionId is not { } sessionId)
                continue;

            var session = await db.AgentSessions.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.TenantId == staleRun.TenantId
                    && candidate.Id == sessionId, ct)
                .ConfigureAwait(false);
            if (session?.Status is not (AgentSessionStatuses.Running or AgentSessionStatuses.PauseRequested))
                continue;

            await runSink.FailAndRejectOrphanedContentAsync(
                    session.TenantId,
                    session.Id,
                    "stale_run_reaped",
                    session.ReplanCount,
                    now,
                    ct)
                .ConfigureAwait(false);
        }

        if (reapedRunCount > 0)
            LogStaleRunsReaped(logger, reapedRunCount);

        // A run reaches the two-hour threshold before its linked session reaches the six-hour
        // threshold. Look at prior reaped runs too, otherwise the zombie session is never revisited.
        // Route every terminalization through the publication fence rather than bulk-updating a session.
        var staleSessions = await db.AgentSessions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => (s.Status == AgentSessionStatuses.Running
                    || s.Status == AgentSessionStatuses.PauseRequested)
                && s.StartedAt < now - StaleSessionAfter
                && db.AgentScheduleRuns.IgnoreQueryFilters().Any(r => r.SessionId == s.Id
                    && r.Status == "failed"
                    && r.Error != null
                    && r.Error.StartsWith("stale_run_reaped")))
            .Select(s => new { s.TenantId, s.Id, s.ReplanCount })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var session in staleSessions)
        {
            await runSink.FailAndRejectOrphanedContentAsync(
                    session.TenantId,
                    session.Id,
                    "stale_run_reaped",
                    session.ReplanCount,
                    now,
                    ct)
                .ConfigureAwait(false);
        }
    }

    private sealed record DueSchedule(Guid Id, DateTimeOffset NextRunAt);

    [LoggerMessage(EventId = 1120, Level = LogLevel.Error, Message = "Failed to run due agent schedule {ScheduleId}")]
    private static partial void LogScheduleRunFailed(ILogger logger, Exception exception, Guid scheduleId);

    [LoggerMessage(
        EventId = 1121,
        Level = LogLevel.Error,
        Message = "Agent schedule tick failed; worker keeps running and retries next tick")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1124, Level = LogLevel.Warning, Message = "Reaped {RunCount} stale agent schedule run(s)")]
    private static partial void LogStaleRunsReaped(ILogger logger, int runCount);
}
