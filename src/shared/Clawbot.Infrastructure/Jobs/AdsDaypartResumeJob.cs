using Clawbot.Agents.Core.Ads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AdsDaypartResumeJob(
    AppDbContext db,
    IAdsConnectorResolver connectorResolver,
    IClock clock,
    ILogger<AdsDaypartResumeJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var campaigns = await db.AdsCampaigns.IgnoreQueryFilters()
            .Where(c => c.DaypartPaused)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var campaign in campaigns)
        {
            try
            {
                var connector = connectorResolver.Resolve(campaign.Platform);
                if (connector is null)
                    continue;

                var applied = await connector.ApplyActionAsync(campaign.TenantId, campaign.ExternalCampaignId, "scale_up", null, ct).ConfigureAwait(false);
                if (applied)
                {
                    campaign.MarkDaypartPaused(false, clock.UtcNow);
                    campaign.Resume(clock.UtcNow);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    LogDaypartResumed(logger, campaign.Id);
                }
            }
            catch (Exception ex)
            {
                LogDaypartResumeFailed(logger, campaign.Id, ex.Message, ex);
            }
        }
    }

    [LoggerMessage(EventId = 5505, Level = LogLevel.Information, Message = "Daypart resumed campaign {CampaignId}")]
    private static partial void LogDaypartResumed(ILogger logger, Guid campaignId);

    [LoggerMessage(EventId = 5515, Level = LogLevel.Warning, Message = "Daypart resume failed for campaign {CampaignId}: {Reason}")]
    private static partial void LogDaypartResumeFailed(ILogger logger, Guid campaignId, string reason, Exception exception);
}
