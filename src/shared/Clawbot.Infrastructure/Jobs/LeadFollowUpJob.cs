using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class LeadFollowUpJob(
    AppDbContext db,
    IClock clock,
    ILogger<LeadFollowUpJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        await ProcessNoShows(now, ct);
        await ProcessStaleLeads(now, ct);
        await ProcessLostLeads(now, ct);
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

        // Lọc lead đã reengage TRƯỚC Take — tránh 30 lead cũ đã reengage "nuốt" slot, lead mới không bao giờ được xử lý.
        var reengagedLeadIds = db.LeadActivities.IgnoreQueryFilters()
            .Where(a => a.ActivityType == "reengage_attempt")
            .Select(a => a.LeadId);

        var staleLeads = await db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.DeletedAt == null
                && l.Stage != "customer"
                && l.Stage != "lost"
                && (l.LastActivityAt ?? l.CreatedAt) < staleThreshold
                && !reengagedLeadIds.Contains(l.Id))
            .OrderBy(l => l.LastActivityAt ?? l.CreatedAt)
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
            var activity = LeadActivity.Create(
                leadInfo.TenantId, leadInfo.Id, "reengage_attempt",
                "Auto: Lead inactive for 30+ days. Queued for re-engagement campaign.",
                now);

            db.LeadActivities.Add(activity);
        }

        await db.SaveChangesAsync(ct);
        LogCompleted(logger, "stale", staleLeads.Count);
    }

    private async Task ProcessLostLeads(DateTimeOffset now, CancellationToken ct)
    {
        var tenantSettings = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.IsActive && t.LeadLostAfterDays > 0)
            .Select(t => new { t.Id, t.LeadLostAfterDays })
            .ToListAsync(ct);

        foreach (var tenant in tenantSettings)
        {
            var threshold = now.AddDays(-tenant.LeadLostAfterDays);
            var reengageCutoff = now.AddDays(-7);
            var farPastThreshold = now.AddDays(-2 * tenant.LeadLostAfterDays);

            // Đưa điều kiện re-engage / 2× threshold vào query trước Take — tránh starvation lead #101.
            var candidates = await db.Leads
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == tenant.Id
                    && l.DeletedAt == null
                    && l.Stage != "customer"
                    && l.Stage != "lost"
                    && (l.LastActivityAt ?? l.CreatedAt) < threshold
                    && (
                        (l.LastActivityAt ?? l.CreatedAt) < farPastThreshold
                        || !db.LeadActivities.IgnoreQueryFilters().Any(a =>
                            a.TenantId == tenant.Id
                            && a.LeadId == l.Id
                            && a.ActivityType == "reengage_attempt"
                            && a.OccurredAt > reengageCutoff)
                    ))
                .OrderBy(l => l.LastActivityAt ?? l.CreatedAt)
                .Take(100)
                .ToListAsync(ct);

            var changed = 0;
            foreach (var lead in candidates)
            {
                // Re-check signal clock: inbound có thể vừa bump LastActivityAt giữa lúc load batch.
                if ((lead.LastActivityAt ?? lead.CreatedAt) >= threshold)
                    continue;

                lead.MarkLost(
                    $"auto: im lặng {tenant.LeadLostAfterDays}+ ngày",
                    now,
                    trigger: "auto_lost_sweep");

                try
                {
                    // Save từng lead: concurrency (Stage + LastActivityAt) khi inbound vừa nhắn → skip lead đó.
                    await db.SaveChangesAsync(ct);
                    changed++;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Detach Lead + mọi Added activity của aggregate (tránh insert stage_change giả ở save sau).
                    foreach (var entry in db.ChangeTracker.Entries()
                                 .Where(e => ReferenceEquals(e.Entity, lead)
                                     || (e.Entity is LeadActivity a && a.LeadId == lead.Id))
                                 .ToList())
                    {
                        entry.State = EntityState.Detached;
                    }

                    LogSkipped(logger, "lost", $"concurrency lead {lead.Id}");
                }
            }

            if (changed == 0)
                continue;

            LogCompleted(logger, "lost", changed);
        }
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
