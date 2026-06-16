using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class LeadFollowUpJob(
    AppDbContext db,
    ILogger<LeadFollowUpJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await ProcessNoShows(now, ct);
        await ProcessStaleLeads(now, ct);
    }

    private async Task ProcessNoShows(DateTimeOffset now, CancellationToken ct)
    {
        var twoHoursAgo = now.AddHours(-2);

        var noShows = await db.LeadActivities
            .IgnoreQueryFilters()
            .Where(a => a.ActivityType == "demo_scheduled" && a.OccurredAt <= twoHoursAgo && a.OccurredAt >= now.AddHours(-6))
            .Where(a => !db.LeadActivities.Any(a2 =>
                a2.LeadId == a.LeadId
                && a2.ActivityType == "demo_attended"
                && a2.OccurredAt > a.OccurredAt))
            .Select(a => a.LeadId)
            .Distinct()
            .Take(30)
            .ToListAsync(ct);

        if (noShows.Count == 0)
        {
            LogSkipped(logger, "no-show", "no demo no-shows found");
            return;
        }

        LogProcessing(logger, "no-show", noShows.Count);

        foreach (var leadId in noShows)
        {
            var lead = await db.Leads.IgnoreQueryFilters()
                .FirstOrDefaultAsync(l => l.Id == leadId, ct);
            if (lead is null) continue;

            var activity = LeadActivity.Create(
                lead.TenantId, leadId, "no_show_followup",
                "Auto: Customer missed scheduled demo. Sending follow-up message.",
                now);

            db.LeadActivities.Add(activity);

            var hasRecentFollowup = await db.LeadActivities.IgnoreQueryFilters()
                .AnyAsync(a => a.LeadId == leadId && a.ActivityType == "no_show_followup"
                    && a.OccurredAt > now.AddHours(-4), ct);

            if (!hasRecentFollowup)
            {
                lead.AdjustScore(-5, "no_show_followup", now);
            }
        }

        await db.SaveChangesAsync(ct);
        LogCompleted(logger, "no-show", noShows.Count);
    }

    private async Task ProcessStaleLeads(DateTimeOffset now, CancellationToken ct)
    {
        var staleThreshold = now.AddDays(-30);

        var staleLeads = await db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.DeletedAt == null
                && l.Stage != "customer"
                && l.Stage != "lost"
                && (l.LastActivityAt == null || l.LastActivityAt < staleThreshold))
            .Select(l => new { l.Id, l.TenantId })
            .Take(30)
            .ToListAsync(ct);

        if (staleLeads.Count == 0)
        {
            LogSkipped(logger, "stale", "no stale leads found");
            return;
        }

        LogProcessing(logger, "stale", staleLeads.Count);

        foreach (var leadInfo in staleLeads)
        {
            var alreadyReengaged = await db.LeadActivities.IgnoreQueryFilters()
                .AnyAsync(a => a.LeadId == leadInfo.Id
                    && a.ActivityType == "reengage_attempt"
                    && a.OccurredAt > now.AddDays(-7), ct);

            if (alreadyReengaged) continue;

            var activity = LeadActivity.Create(
                leadInfo.TenantId, leadInfo.Id, "reengage_attempt",
                "Auto: Lead inactive for 30+ days. Queued for re-engagement campaign.",
                now);

            db.LeadActivities.Add(activity);
        }

        await db.SaveChangesAsync(ct);
        LogCompleted(logger, "stale", staleLeads.Count);
    }

    [LoggerMessage(EventId = 13001, Level = LogLevel.Debug,
        Message = "LeadFollowUp ({Type}) skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string type, string reason);

    [LoggerMessage(EventId = 13002, Level = LogLevel.Information,
        Message = "LeadFollowUp ({Type}) processing {Count} leads")]
    private static partial void LogProcessing(ILogger logger, string type, int count);

    [LoggerMessage(EventId = 13003, Level = LogLevel.Information,
        Message = "LeadFollowUp ({Type}) completed: {Count} leads processed")]
    private static partial void LogCompleted(ILogger logger, string type, int count);
}
