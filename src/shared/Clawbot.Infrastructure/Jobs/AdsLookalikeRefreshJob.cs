using Clawbot.Agents.Core.Ads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AdsLookalikeRefreshJob(
    AppDbContext db,
    IAdsConnectorResolver connectorResolver,
    INotificationPublisher publisher,
    ILogger<AdsLookalikeRefreshJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
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

                var seedKeys = await db.Leads.IgnoreQueryFilters()
                    .Where(l => l.TenantId == tenantId && (l.Stage == "hot" || l.Stage == "customer"))
                    .Join(
                        db.Contacts.IgnoreQueryFilters().Where(c => c.TenantId == tenantId),
                        l => l.ContactId,
                        c => c.Id,
                        (l, c) => c.Phone ?? c.Email ?? string.Empty)
                    .Where(k => k != string.Empty)
                    .Distinct()
                    .ToListAsync(ct).ConfigureAwait(false);

                if (seedKeys.Count < 100)
                {
                    LogSeedSkipped(logger, platform, seedKeys.Count);
                    continue;
                }

                var audienceId = await connector.BuildLookalikeAsync(tenantId, seedKeys, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(audienceId))
                {
                    await publisher.PublishAsync(new NotificationRequest(
                        TenantId: tenantId,
                        UserId: null,
                        Type: "ads_lookalike_failed",
                        Title: $"Lookalike audience not created for {platform}",
                        Severity: "warning",
                        Body: $"Connector returned no audience id for {platform} with {seedKeys.Count} seed contact(s). Check vendor credentials and audience permissions.",
                        Link: "/ads"), ct).ConfigureAwait(false);
                    LogLookalikeFailedWithoutAudience(logger, platform, seedKeys.Count);
                    continue;
                }

                LogLookalikeBuilt(logger, platform, audienceId, seedKeys.Count);
            }
            catch (Exception ex)
            {
                LogLookalikeFailed(logger, platform, ex.Message, ex);
            }
        }
    }

    [LoggerMessage(EventId = 5507, Level = LogLevel.Information, Message = "Lookalike seed for {Platform}: {Count} contacts (< 100, skipping)")]
    private static partial void LogSeedSkipped(ILogger logger, string platform, int count);

    [LoggerMessage(EventId = 5508, Level = LogLevel.Information, Message = "Lookalike for {Platform}: audience={AudienceId}, seed={Count}")]
    private static partial void LogLookalikeBuilt(ILogger logger, string platform, string? audienceId, int count);

    [LoggerMessage(EventId = 5509, Level = LogLevel.Warning, Message = "Lookalike for {Platform} returned no audience id, seed={Count}")]
    private static partial void LogLookalikeFailedWithoutAudience(ILogger logger, string platform, int count);

    [LoggerMessage(EventId = 5517, Level = LogLevel.Warning, Message = "Lookalike refresh failed for {Platform}: {Reason}")]
    private static partial void LogLookalikeFailed(ILogger logger, string platform, string reason, Exception exception);
}
