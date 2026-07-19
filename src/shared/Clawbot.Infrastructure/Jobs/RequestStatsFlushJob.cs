using Clawbot.Domain.Observability;
using Clawbot.Infrastructure.Observability;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// Flushes in-memory RequestStatsCounter into dbo.request_stats_hourly every minute.
/// </summary>
public sealed partial class RequestStatsFlushJob(
    AppDbContext db,
    RequestStatsCounter counter,
    ILogger<RequestStatsFlushJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var batch = counter.SnapshotAndReset();
        if (batch.Count == 0) return;

        foreach (var row in batch)
        {
            var existing = await db.RequestStatsHourly
                .FirstOrDefaultAsync(
                    x => x.BucketHour == row.BucketHour
                         && x.TenantId == row.TenantId
                         && x.StatusClass == row.StatusClass,
                    ct)
                .ConfigureAwait(false);

            if (existing is null)
                db.RequestStatsHourly.Add(
                    RequestStatsHourly.Create(row.BucketHour, row.TenantId, row.StatusClass, row.Count));
            else
                existing.Add(row.Count);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogFlushed(logger, batch.Count);
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Debug, Message = "Flushed {Count} request-stats buckets")]
    private static partial void LogFlushed(ILogger logger, int count);
}
