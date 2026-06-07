using Clawbot.Agents.Core.Ads;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AdsLookalikeRefreshJob(
    AppDbContext db,
    IAdsConnectorResolver connectorResolver,
    ILogger<AdsLookalikeRefreshJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var campaigns = await db.AdsCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status == "ACTIVE")
            .GroupBy(c => c.Platform)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var group in campaigns)
        {
            var platform = group.Key;
            try
            {
                var connector = connectorResolver.Resolve(platform);
                if (connector is null)
                    continue;

                var seedKeys = await db.Leads.IgnoreQueryFilters()
                    .Where(l => l.Stage == "hot" || l.Stage == "won")
                    .Join(db.Contacts.IgnoreQueryFilters(),
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

                var audienceId = await connector.BuildLookalikeAsync(seedKeys, ct).ConfigureAwait(false);
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

    [LoggerMessage(EventId = 5517, Level = LogLevel.Warning, Message = "Lookalike refresh failed for {Platform}: {Reason}")]
    private static partial void LogLookalikeFailed(ILogger logger, string platform, string reason, Exception exception);
}
