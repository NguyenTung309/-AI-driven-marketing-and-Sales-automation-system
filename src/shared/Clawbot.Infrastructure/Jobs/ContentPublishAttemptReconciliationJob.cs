using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// Phase 3.9: scan outcome_unknown publish attempts. Never blind-retries the provider;
/// keeps durable state for provider-native reconciliation or privileged content:publish decision.
/// </summary>
public sealed partial class ContentPublishAttemptReconciliationJob(
    AppDbContext db,
    IClock clock,
    ILogger<ContentPublishAttemptReconciliationJob> logger)
{
    private const int BatchSize = 50;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var attempts = await db.ContentPublishAttempts
            .IgnoreQueryFilters()
            .Where(attempt => attempt.Status == ContentPublishAttempt.StatusOutcomeUnknown)
            .OrderBy(attempt => attempt.CompletedAt)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (attempts.Count == 0)
        {
            LogSkipped(logger, "no outcome_unknown attempts");
            return;
        }

        foreach (var attempt in attempts)
        {
            // Provider-native lookup is not available for all adapters yet — surface and leave durable.
            // Privileged content:publish paths reconcile after operator verification.
            LogNeedsReconciliation(
                logger,
                attempt.TenantId,
                attempt.Id,
                attempt.ScheduleId,
                attempt.ContentItemId,
                attempt.LastErrorCode ?? "publish_outcome_unknown",
                now);
        }
    }

    [LoggerMessage(
        EventId = 5120,
        Level = LogLevel.Information,
        Message = "Content publish reconciliation skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 5121,
        Level = LogLevel.Warning,
        Message = "Publish attempt {AttemptId} tenant {TenantId} schedule {ScheduleId} item {ContentItemId} needs reconciliation ({ErrorCode}) at {At}")]
    private static partial void LogNeedsReconciliation(
        ILogger logger,
        Guid tenantId,
        Guid attemptId,
        Guid scheduleId,
        Guid contentItemId,
        string errorCode,
        DateTimeOffset at);
}