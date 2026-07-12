using Clawbot.Agents.Core.Ads;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AdsRemarketingJob(
    AppDbContext db,
    IAdsConnectorResolver connectorResolver,
    ILogger<AdsRemarketingJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var campaigns = await db.AdsCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status == "ACTIVE")
            .GroupBy(c => new { c.TenantId, c.Platform })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var group in campaigns)
        {
            var tenantId = group.Key.TenantId;
            var platform = group.Key.Platform;
            try
            {
                var connector = connectorResolver.Resolve(platform);
                if (connector is null)
                    continue;

                var coldLeads = await db.Leads.IgnoreQueryFilters()
                    .Where(l => l.TenantId == tenantId && l.Stage == "cold")
                    .Join(db.Contacts.IgnoreQueryFilters(),
                        l => l.ContactId,
                        c => c.Id,
                        (l, c) => c.Phone ?? c.Email ?? string.Empty)
                    .Where(k => k != string.Empty)
                    .Distinct()
                    .Take(1000)
                    .ToListAsync(ct).ConfigureAwait(false);

                if (coldLeads.Count == 0)
                    continue;

                var success = await connector.BuildRemarketingAsync(
                    tenantId, $"cold-leads-{platform}", coldLeads, ct).ConfigureAwait(false);

                LogRemarketing(logger, platform, coldLeads.Count, success);
            }
            catch (Exception ex)
            {
                LogRemarketingFailed(logger, platform, ex.Message, ex);
            }
        }
    }

    [LoggerMessage(EventId = 5506, Level = LogLevel.Information, Message = "Remarketing for {Platform}: {Count} contacts, success={Success}")]
    private static partial void LogRemarketing(ILogger logger, string platform, int count, bool success);

    [LoggerMessage(EventId = 5516, Level = LogLevel.Warning, Message = "Remarketing failed for {Platform}: {Reason}")]
    private static partial void LogRemarketingFailed(ILogger logger, string platform, string reason, Exception exception);
}
