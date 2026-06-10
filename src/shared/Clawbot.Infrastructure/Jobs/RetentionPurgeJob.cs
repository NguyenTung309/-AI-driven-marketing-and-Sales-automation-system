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

        // PII retention: null out raw message content >30d, keep the redacted copy (NFR PII-30d).
        var scrubbed = await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.SentAt < cutoff && m.OriginalContent != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.OriginalContent, (string?)null), ct)
            .ConfigureAwait(false);
        LogMessagesScrubbed(_logger, scrubbed, cutoff);
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Retention purge removed {Count} audit_logs before {Cutoff:o}")]
    private static partial void LogPurged(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Retention purge scrubbed original_content on {Count} messages before {Cutoff:o}")]
    private static partial void LogMessagesScrubbed(ILogger logger, int count, DateTimeOffset cutoff);
}
