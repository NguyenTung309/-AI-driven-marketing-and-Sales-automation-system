using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed class AgentScheduleRunner(
    AppDbContext db,
    AutonomousOrchestrator orchestrator,
    ITenantTrendScanner trendScanner,
    IClock clock)
{
    private readonly AppDbContext _db = db;
    private readonly AutonomousOrchestrator _orchestrator = orchestrator;
    private readonly ITenantTrendScanner _trendScanner = trendScanner;
    private readonly IClock _clock = clock;

    public async Task<AgentScheduleRun?> RunDueAsync(Guid scheduleId, DateTimeOffset dueAtUtc, CancellationToken ct = default)
    {
        var schedule = await _db.AgentSchedules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (schedule is null || !schedule.IsActive)
            return null;

        // C2: event-triggered schedules — mỗi lần dispatcher kéo NextRunAt về now là một window riêng
        // (ticks-based), và sau khi chạy thì ngủ tới sự kiện kế tiếp thay vì lặp theo cadence.
        var isEventTriggered = string.Equals(schedule.TriggerType, "event", StringComparison.OrdinalIgnoreCase);
        var windowKey = isEventTriggered
            ? $"event:{dueAtUtc.UtcTicks}"
            : RecurrenceCalculator.WindowKey(schedule.Cadence, dueAtUtc, schedule.TimezoneId);
        var existing = await _db.AgentScheduleRuns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ScheduleId == schedule.Id && r.WindowKey == windowKey, ct)
            .ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var nextRunAt = isEventTriggered
            ? DateTimeOffset.MaxValue
            : RecurrenceCalculator.NextRunUtc(schedule.Cadence, dueAtUtc, schedule.TimezoneId);
        var now = _clock.UtcNow;
        var run = AgentScheduleRun.Start(schedule.TenantId, schedule.Id, windowKey, now);
        if (await HasStartedRunAsync(schedule.Id, ct).ConfigureAwait(false))
        {
            run.SkipOverlap(now);
            _db.AgentScheduleRuns.Add(run);
            schedule.RecordRun(dueAtUtc, nextRunAt, now);
            var overlapDuplicate = await SaveOrGetDuplicateAsync(schedule, run, null, ct).ConfigureAwait(false);
            return overlapDuplicate ?? run;
        }

        if (IsTrendScan(schedule))
        {
            _db.AgentScheduleRuns.Add(run);
            schedule.RecordRun(dueAtUtc, nextRunAt, now);
            var trendDuplicate = await SaveOrGetDuplicateAsync(schedule, run, null, ct).ConfigureAwait(false);
            if (trendDuplicate is not null)
                return trendDuplicate;

            await ExecuteTrendScanAsync(schedule.TenantId, run, ct).ConfigureAwait(false);
            return run;
        }

        var session = AgentSession.CreatePlan(schedule.TenantId, schedule.GoalTemplate, "{}", schedule.RequiresApproval, now);
        run.LinkSession(session.Id);
        _db.AgentSessions.Add(session);
        _db.AgentScheduleRuns.Add(run);
        schedule.RecordRun(dueAtUtc, nextRunAt, now);
        var duplicate = await SaveOrGetDuplicateAsync(schedule, run, session, ct).ConfigureAwait(false);
        if (duplicate is not null)
            return duplicate;

        try
        {
            var result = await _orchestrator.RunAsync(
                new AutonomousRunRequest(schedule.TenantId, session.Id, schedule.GoalTemplate, "schedule", schedule.RequiresApproval),
                ct).ConfigureAwait(false);
            if (result.Status == "completed")
                run.Complete(_clock.UtcNow);
            else
                run.Fail(result.Reason ?? result.Status, _clock.UtcNow);
        }
        catch (OperationCanceledException)
        {
            run.Cancel(_clock.UtcNow);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentScheduleRun?> RunNowAsync(Guid scheduleId, CancellationToken ct = default) =>
        await RunManualAsync(scheduleId, $"manual:{_clock.UtcNow:yyyyMMddHHmmssfffffff}", ct).ConfigureAwait(false);

    private async Task<AgentScheduleRun?> RunManualAsync(Guid scheduleId, string windowKey, CancellationToken ct)
    {
        var schedule = await _db.AgentSchedules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (schedule is null)
            return null;

        var now = _clock.UtcNow;
        var run = AgentScheduleRun.Start(schedule.TenantId, schedule.Id, windowKey, now);
        if (IsTrendScan(schedule))
        {
            _db.AgentScheduleRuns.Add(run);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await ExecuteTrendScanAsync(schedule.TenantId, run, ct).ConfigureAwait(false);
            return run;
        }

        var session = AgentSession.CreatePlan(schedule.TenantId, schedule.GoalTemplate, "{}", schedule.RequiresApproval, now);
        run.LinkSession(session.Id);
        _db.AgentSessions.Add(session);
        _db.AgentScheduleRuns.Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            var result = await _orchestrator.RunAsync(
                new AutonomousRunRequest(schedule.TenantId, session.Id, schedule.GoalTemplate, "manual", schedule.RequiresApproval),
                ct).ConfigureAwait(false);
            if (result.Status == "completed")
                run.Complete(_clock.UtcNow);
            else
                run.Fail(result.Reason ?? result.Status, _clock.UtcNow);
        }
        catch (OperationCanceledException)
        {
            run.Cancel(_clock.UtcNow);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
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
        catch (DbUpdateException)
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
            throw;
        }
    }

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
            run.Cancel(_clock.UtcNow);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            // A run left in "started" would block every future window via the overlap check.
            run.Fail(ex.Message, _clock.UtcNow);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> HasStartedRunAsync(Guid scheduleId, CancellationToken ct) =>
        await _db.AgentScheduleRuns.IgnoreQueryFilters()
            .AnyAsync(r => r.ScheduleId == scheduleId && r.Status == "started" && r.FinishedAt == null, ct)
            .ConfigureAwait(false);
}
