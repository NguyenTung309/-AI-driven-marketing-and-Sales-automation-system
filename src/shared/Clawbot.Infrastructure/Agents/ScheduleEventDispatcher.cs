using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

/// <summary>
/// C2: fires event-triggered schedules by pulling NextRunAt to now. Reuses the schedule worker's
/// normal due-run path (idempotency window + overlap-skip in AgentScheduleRunner) instead of a
/// second execution pipeline. Callers pass their own AppDbContext and save alongside their unit of work.
/// </summary>
public static class ScheduleEventDispatcher
{
    public static async Task<int> FireAsync(
        AppDbContext db,
        Guid tenantId,
        string eventKey,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var normalized = eventKey.Trim().ToLowerInvariant();
        var schedules = (await db.AgentSchedules.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId
                && s.DeletedAt == null
                && s.IsActive
                && s.TriggerType == "event"
                && s.EventKey == normalized)
            .ToListAsync(ct).ConfigureAwait(false))
            // SQLite (test provider) không dịch được so sánh DateTimeOffset — lọc client-side; tập event schedules nhỏ.
            .Where(s => s.NextRunAt > now)
            .ToList();

        foreach (var schedule in schedules)
        {
            schedule.UpdateSchedule(
                schedule.Name, schedule.GoalTemplate, schedule.Cadence, schedule.CronExpression,
                schedule.TimezoneId, now, schedule.RequiresApproval, schedule.OverlapPolicy,
                schedule.MisfirePolicy, schedule.ApprovalPolicyJson, now);
        }

        if (schedules.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return schedules.Count;
    }
}
