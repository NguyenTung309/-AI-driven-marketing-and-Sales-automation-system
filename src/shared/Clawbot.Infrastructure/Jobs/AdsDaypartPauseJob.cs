using Clawbot.Agents.Core.Ads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AdsDaypartPauseJob(
    AppDbContext db,
    IAdsConnectorResolver connectorResolver,
    IClock clock,
    ILogger<AdsDaypartPauseJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var campaigns = await db.AdsCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status == "ACTIVE" && !c.DaypartPaused)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var campaign in campaigns)
        {
            try
            {
                var connector = connectorResolver.Resolve(campaign.Platform);
                if (connector is null)
                    continue;

                var applied = await connector.ApplyActionAsync(campaign.TenantId, campaign.ExternalCampaignId, "pause", null, ct).ConfigureAwait(false);
                if (applied)
                {
                    campaign.MarkDaypartPaused(true, clock.UtcNow);
                    campaign.Pause(clock.UtcNow);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    LogDaypartPaused(logger, campaign.Id);
                }
            }
            catch (Exception ex)
            {
                LogDaypartPauseFailed(logger, campaign.Id, ex.Message, ex);
            }
        }
    }

    [LoggerMessage(EventId = 5504, Level = LogLevel.Information, Message = "Daypart paused campaign {CampaignId}")]
    private static partial void LogDaypartPaused(ILogger logger, Guid campaignId);

    [LoggerMessage(EventId = 5514, Level = LogLevel.Warning, Message = "Daypart pause failed for campaign {CampaignId}: {Reason}")]
    private static partial void LogDaypartPauseFailed(ILogger logger, Guid campaignId, string reason, Exception exception);
}
