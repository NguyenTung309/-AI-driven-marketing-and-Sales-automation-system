using Clawbot.Agents.Core.Ads;
using Clawbot.Domain.Ads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AdsCreativeRotationJob(
    AppDbContext db,
    IAdsConnectorResolver connectorResolver,
    IClock clock,
    ILogger<AdsCreativeRotationJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var campaigns = await db.AdsCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status == "ACTIVE")
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var campaign in campaigns)
        {
            try
            {
                var creatives = await db.AdsCreatives.IgnoreQueryFilters()
                    .Where(c => c.CampaignId == campaign.Id)
                    .ToListAsync(ct).ConfigureAwait(false);

                if (creatives.Count < 2)
                    continue;

                var active = creatives.Where(c => c.Status == "active").ToList();
                var standby = creatives.Where(c => c.Status == "standby").ToList();

                if (active.Count == 0 || standby.Count == 0)
                    continue;

                var fatigued = active[0];
                var replacement = standby[0];

                fatigued.Standby(clock.UtcNow);
                replacement.Activate(clock.UtcNow);

                var connector = connectorResolver.Resolve(campaign.Platform);
                if (connector is not null)
                    await connector.ApplyActionAsync(campaign.TenantId, campaign.ExternalCampaignId, "rotate", null, ct).ConfigureAwait(false);

                LogRotated(logger, fatigued.Id, replacement.Id, campaign.Id);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRotationFailed(logger, campaign.Id, ex.Message, ex);
            }
        }
    }

    [LoggerMessage(EventId = 5503, Level = LogLevel.Information, Message = "Rotated creative {FatiguedId} → standby, {ReplacementId} → active for campaign {CampaignId}")]
    private static partial void LogRotated(ILogger logger, Guid fatiguedId, Guid replacementId, Guid campaignId);

    [LoggerMessage(EventId = 5513, Level = LogLevel.Warning, Message = "Creative rotation failed for campaign {CampaignId}: {Reason}")]
    private static partial void LogRotationFailed(ILogger logger, Guid campaignId, string reason, Exception exception);
}
