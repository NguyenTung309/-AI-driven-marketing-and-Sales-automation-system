using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class RetentionPurgeJob(
    AppDbContext db,
    ILogger<RetentionPurgeJob> logger,
    TimeProvider? clock = null)
{
    private const int SensitiveDataRetentionDays = 30;
    private const int NotificationRetentionDays = 90;

    private readonly AppDbContext _db = db;
    private readonly ILogger<RetentionPurgeJob> _logger = logger;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var cutoff = now.AddDays(-SensitiveDataRetentionDays);
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

        var notificationCutoff = now.AddDays(-NotificationRetentionDays);
        var notificationsRemoved = await _db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.CreatedAt < notificationCutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogNotificationsPurged(_logger, notificationsRemoved, notificationCutoff);
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Retention purge removed {Count} audit_logs before {Cutoff:o}")]
    private static partial void LogPurged(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Retention purge scrubbed original_content on {Count} messages before {Cutoff:o}")]
    private static partial void LogMessagesScrubbed(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information, Message = "Retention purge removed {Count} notifications before {Cutoff:o}")]
    private static partial void LogNotificationsPurged(ILogger logger, int count, DateTimeOffset cutoff);
}
