using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Infrastructure.Observability;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class RetentionPurgeJob(
    AppDbContext db,
    IPiiRedactor pii,
    ILogger<RetentionPurgeJob> logger,
    TimeProvider? clock = null,
    IOptions<SystemLogsOptions>? systemLogsOptions = null,
    IOptions<AuditRetentionOptions>? auditOptions = null)
{
    private const int DefaultSystemLogRetentionDays = 30;
    private const int DefaultAuditRetentionDays = 180;
    private const int SensitiveDataRetentionDays = 30;
    private const int NotificationRetentionDays = 90;
    private const int ContentWorkflowMetricsRetentionDays = 180;
    // Prompt chaining P5 (§6): trace chuỗi sinh nội dung giữ 30 ngày — đủ so chất lượng khi sửa prompt, không phình.
    private const int ContentGenerationTraceRetentionDays = 30;

    private readonly AppDbContext _db = db;
    private readonly IPiiRedactor _pii = pii;
    private readonly ILogger<RetentionPurgeJob> _logger = logger;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SystemLogsOptions _systemLogs = systemLogsOptions?.Value ?? new SystemLogsOptions();
    private readonly AuditRetentionOptions _audit = auditOptions?.Value ?? new AuditRetentionOptions();

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();

        var auditDays = _audit.RetentionDays > 0 ? _audit.RetentionDays : DefaultAuditRetentionDays;
        var auditCutoff = now.AddDays(-auditDays);
        var removed = await _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.OccurredAt < auditCutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogPurged(_logger, removed, auditCutoff);

        var systemDays = _systemLogs.RetentionDays > 0 ? _systemLogs.RetentionDays : DefaultSystemLogRetentionDays;
        var systemCutoff = now.AddDays(-systemDays);
        var systemRemoved = await _db.SystemLogs
            .Where(l => l.OccurredAt < systemCutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogSystemLogsPurged(_logger, systemRemoved, systemCutoff);

        var cutoff = now.AddDays(-SensitiveDataRetentionDays);

        // PII retention: re-redact every historical row before dropping raw content. This also repairs
        // legacy/widget rows whose Content or RedactedContent was persisted before the split invariant existed.
        var messageIds = await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.SentAt < cutoff)
            .Select(m => m.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var scrubbed = 0;
        foreach (var batch in messageIds.Chunk(500))
        {
            var messages = await _db.Messages
                .IgnoreQueryFilters()
                .Where(m => batch.Contains(m.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var message in messages)
            {
                var source = message.OriginalContent ?? message.RedactedContent ?? message.Content;
                var redacted = await _pii.RedactAsync(source, ct).ConfigureAwait(false);
                message.ScrubOriginalContent(redacted.RedactedText);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            scrubbed += messages.Count;
        }
        LogMessagesScrubbed(_logger, scrubbed, cutoff);

        var notificationCutoff = now.AddDays(-NotificationRetentionDays);
        var notificationsRemoved = await _db.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.CreatedAt < notificationCutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogNotificationsPurged(_logger, notificationsRemoved, notificationCutoff);

        var contentMetricsCutoff = now.AddDays(-ContentWorkflowMetricsRetentionDays);
        var contentMetricsRemoved = await _db.ContentWorkflowMetricsHourly
            .IgnoreQueryFilters()
            .Where(metrics => metrics.HourUtc < contentMetricsCutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogContentWorkflowMetricsPurged(_logger, contentMetricsRemoved, contentMetricsCutoff);

        var traceCutoff = now.AddDays(-ContentGenerationTraceRetentionDays);
        var tracesRemoved = await _db.ContentGenerationTraces
            .IgnoreQueryFilters()
            .Where(trace => trace.CreatedAt < traceCutoff)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogContentGenerationTracesPurged(_logger, tracesRemoved, traceCutoff);
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Retention purge removed {Count} audit_logs before {Cutoff:o}")]
    private static partial void LogPurged(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Retention purge scrubbed original_content on {Count} messages before {Cutoff:o}")]
    private static partial void LogMessagesScrubbed(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information, Message = "Retention purge removed {Count} notifications before {Cutoff:o}")]
    private static partial void LogNotificationsPurged(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information, Message = "Retention purge removed {Count} system_logs before {Cutoff:o}")]
    private static partial void LogSystemLogsPurged(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Information, Message = "Retention purge removed {Count} content workflow metric rows before {Cutoff:o}")]
    private static partial void LogContentWorkflowMetricsPurged(
        ILogger logger,
        int count,
        DateTimeOffset cutoff);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Information, Message = "Retention purge removed {Count} content_generation_traces before {Cutoff:o}")]
    private static partial void LogContentGenerationTracesPurged(
        ILogger logger,
        int count,
        DateTimeOffset cutoff);
}
