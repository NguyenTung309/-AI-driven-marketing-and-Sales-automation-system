using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class WeeklyAdsReportJob(
    AppDbContext db,
    IClock clock,
    ILogger<WeeklyAdsReportJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);

        var campaigns = await db.AdsCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status != null)
            .GroupBy(c => c.TenantId)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var group in campaigns)
        {
            var tenantId = group.Key;
            var campaignIds = group.Select(c => c.Id).ToList();

            var metrics = await db.AdsMetricsDailies.IgnoreQueryFilters()
                .Where(m => campaignIds.Contains(m.CampaignId) && m.MetricDate >= DateOnly.FromDateTime(weekAgo.DateTime))
                .ToListAsync(ct).ConfigureAwait(false);

            var actions = await db.AdsActions.IgnoreQueryFilters()
                .Where(a => campaignIds.Contains(a.CampaignId) && a.ExecutedAt >= weekAgo)
                .CountAsync(ct).ConfigureAwait(false);

            var totalSpend = metrics.Sum(m => m.Spend ?? 0);
            var avgCpl = metrics.Where(m => m.Cpl > 0).Select(m => m.Cpl ?? 0).DefaultIfEmpty(0).Average();

            LogWeeklyReport(logger, tenantId, group.Count(), totalSpend, avgCpl, actions);
        }
    }

    [LoggerMessage(EventId = 5509, Level = LogLevel.Information, Message = "Weekly ads report for tenant {TenantId}: {CampaignCount} campaigns, spend={TotalSpend}, avgCpl={AvgCpl}, actions={Actions}")]
    private static partial void LogWeeklyReport(ILogger logger, Guid tenantId, int campaignCount, decimal totalSpend, decimal avgCpl, int actions);
}
