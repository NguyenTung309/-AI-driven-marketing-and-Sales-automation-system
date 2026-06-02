using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class RetentionPurgeJob(AppDbContext db, ILogger<RetentionPurgeJob> logger)
{
    private const int RetentionDays = 30;

    private readonly AppDbContext _db = db;
    private readonly ILogger<RetentionPurgeJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        var removed = await _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.OccurredAt < cutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogPurged(_logger, removed, cutoff);
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Retention purge removed {Count} audit_logs before {Cutoff:o}")]
    private static partial void LogPurged(ILogger logger, int count, DateTimeOffset cutoff);
}
