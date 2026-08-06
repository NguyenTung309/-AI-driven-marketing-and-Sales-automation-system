using Clawbot.Domain.Analytics;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Analytics;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class DailyKpiRollupJob(
    AppDbContext db,
    IKpiAggregator aggregator,
    IClock clock,
    ILogger<DailyKpiRollupJob> logger)
{
    private static readonly TimeSpan AnalyticsOffset = TimeSpan.FromHours(7);

    private readonly AppDbContext _db = db;
    private readonly IKpiAggregator _aggregator = aggregator;
    private readonly IClock _clock = clock;
    private readonly ILogger<DailyKpiRollupJob> _logger = logger;

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [3600, 3600, 3600])]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var metricDate = DateOnly.FromDateTime(_clock.UtcNow.ToOffset(AnalyticsOffset).DateTime).AddDays(-1);

        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        var inserted = 0;
        foreach (var tenantId in tenants)
        {
            try
            {
                var rows = await _aggregator.AggregateDailyAsync(tenantId, metricDate, ct).ConfigureAwait(false);
                foreach (var row in rows)
                {
                    var existing = await _db.KpiDailies.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Date == metricDate && k.Platform == row.Platform, ct)
                        .ConfigureAwait(false);

                    if (existing is null)
                    {
                        existing = KpiDaily.Create(tenantId, metricDate, row.Platform, _clock.UtcNow);
                        _db.KpiDailies.Add(existing);
                        inserted++;
                    }

                    existing.Record(
                        row.Leads,
                        row.Dms,
                        row.Replies,
                        row.RepliedDms,
                        row.Conversions,
                        row.AvgResponseTimeSec,
                        row.AdSpend,
                        row.Revenue);
                }

                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRollupFailed(_logger, ex, tenantId, metricDate);
                _db.AuditLogs.Add(AuditLog.Create(
                    tenantId,
                    userId: null,
                    action: "kpi_rollup_failed",
                    resourceType: "kpi_daily",
                    resourceId: null,
                    occurredAt: _clock.UtcNow,
                    diffJson: JsonSerializer.Serialize(new { metricDate, error = ex.Message })));
                await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        LogRolledUp(_logger, inserted, metricDate);
    }

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Daily KPI rollup processed (new={Count}) for {Day}")]
    private static partial void LogRolledUp(ILogger logger, int count, DateOnly day);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Error, Message = "Daily KPI rollup failed for tenant {TenantId} on {Day}")]
    private static partial void LogRollupFailed(ILogger logger, Exception exception, Guid tenantId, DateOnly day);
}
