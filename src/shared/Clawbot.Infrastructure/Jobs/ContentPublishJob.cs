using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class ContentPublishJob(
    AppDbContext db,
    ISocialPublisher publisher,
    IContentNotifier notifier,
    IContentWorkflowRuntimeGate runtimeGate,
    IClock clock,
    ILogger<ContentPublishJob> logger)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan AmbiguousOutcomeSaveTimeout = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _db = db;
    private readonly ISocialPublisher _publisher = publisher;
    private readonly IContentNotifier _notifier = notifier;
    private readonly IContentWorkflowRuntimeGate _runtimeGate = runtimeGate;
    private readonly IClock _clock = clock;
    private readonly ILogger<ContentPublishJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        // Phase 6.2: fail closed while cutover pause is active (SQL triggers also fence claims).
        if (await _runtimeGate.IsPublicationPausedAsync(ct).ConfigureAwait(false))
        {
            LogPublicationPaused(_logger);
            return;
        }

        var now = _clock.UtcNow;
        var pendingSchedules = await _db.ContentSchedules.IgnoreQueryFilters()
            .Where(schedule => schedule.Status == ContentSchedule.StatusPending)
            .ToListAsync(ct).ConfigureAwait(false);
        var dueSchedules = pendingSchedules
            .Where(schedule => schedule.ScheduledAt <= now)
            .OrderBy(schedule => schedule.ScheduledAt)
            .Take(BatchSize)
            .ToList();
        if (dueSchedules.Count == 0)
            return;

        var contentItemIds = dueSchedules.Select(schedule => schedule.ContentItemId).ToList();
        var itemsById = await _db.ContentItems.IgnoreQueryFilters()
            .Where(item => contentItemIds.Contains(item.Id) && item.DeletedAt == null)
            .ToDictionaryAsync(item => item.Id, ct).ConfigureAwait(false);

        foreach (var schedule in dueSchedules)
        {
            if (!itemsById.TryGetValue(schedule.ContentItemId, out var item) || item.TenantId != schedule.TenantId)
            {
                // Item missing / hard-deleted — free the unique pending index so user can re-schedule.
                schedule.Cancel(now, "item_missing");
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                continue;
            }

            // Stale schedule: item was reverted/rejected after scheduling — cancel so it no longer blocks
            // the unique pending index and calendar shows a terminal state with reason.
            if (!string.Equals(item.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                var staleReason = ContentSchedule.ErrorStaleItemPrefix + item.Status;
                schedule.Cancel(now, staleReason);
                LogStaleScheduleSkipped(_logger, schedule.TenantId, item.Id, schedule.Id, item.Status);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                continue;
            }

            if (schedule.ContentRevision != item.ContentRevision)
            {
                schedule.Cancel(now, "stale_content_revision");
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                continue;
            }

            await AttemptPublishAsync(schedule, item, now, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    // Manual retry / publish-now from API. Returns (success, error).
    public async Task<(bool Success, string? Error)> PublishOneAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var schedule = await _db.ContentSchedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct).ConfigureAwait(false);
        if (schedule is null)
            return (false, "schedule_not_found");

        if (!string.Equals(schedule.Status, ContentSchedule.StatusPending, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(schedule.Status, ContentSchedule.StatusHeld, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(schedule.Status, ContentSchedule.StatusFailed, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "schedule_not_retryable");
        }

        var item = await _db.ContentItems
            .FirstOrDefaultAsync(i => i.Id == schedule.ContentItemId && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null || item.TenantId != schedule.TenantId)
            return (false, "item_not_found");

        // Failed schedule may have item still scheduled, or approved after cancel-less fail.
        if (string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase))
            item.MarkScheduled(now);
        else if (!string.Equals(item.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            return (false, $"item_not_schedulable:{item.Status}");

        if (!schedule.TryResetForRetry(now))
            return (false, "schedule_not_retryable");

        if (schedule.ContentRevision != item.ContentRevision)
        {
            schedule.Cancel(now, "stale_content_revision");
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return (false, "stale_content_revision");
        }

        await AttemptPublishAsync(schedule, item, now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return string.Equals(schedule.Status, ContentSchedule.StatusPosted, StringComparison.OrdinalIgnoreCase)
            ? (true, null)
            : (false, schedule.LastError ?? "publisher_failed");
    }

    private async Task AttemptPublishAsync(
        ContentSchedule schedule,
        ContentItem item,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var holdReason = ResolvePublishHoldReason(schedule, item);
        if (holdReason is not null)
        {
            schedule.MarkHeld(holdReason, now);
            return;
        }

        // Phase 3.7: conditional claim freezes immutable snapshot + stable idempotency before external call.
        var publishTargetId = schedule.PublishTargetId
            ?? schedule.MetaAssetId
            ?? schedule.Id;
        var assetSnapshots = await LoadReadyAssetSnapshotsAsync(
            schedule.TenantId,
            item.Id,
            ct).ConfigureAwait(false);
        var leaseExpiresAt = now.AddMinutes(10);
        var attempt = ContentPublishAttempt.Claim(
            schedule.TenantId,
            schedule.Id,
            item.Id,
            item.ContentRevision,
            schedule.Platform,
            publishTargetId,
            item.Body,
            assetSnapshots,
            leaseExpiresAt,
            now,
            attemptSequence: schedule.RetryCount + 1);
        item.ClaimPublishAttempt(item.ContentRevision, attempt.Id, now);
        schedule.MarkPublishing(now);
        _db.ContentPublishAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Mark transmitted before provider call so timeout/process-loss becomes outcome_unknown, not blind retry.
        attempt.MarkTransmitted(attempt.LeaseToken!.Value, providerRequestId: null, now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var request = new PublishRequest(
            schedule.TenantId,
            item.Id,
            schedule.Platform,
            attempt.BodySnapshot,
            attempt.AssetsSnapshotJson,
            schedule.ScheduledAt,
            schedule.MetaAssetId);
        PublishResult result;
        try
        {
            result = await _publisher.PublishAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            attempt.MarkOutcomeUnknown(attempt.LeaseToken!.Value, "publish_outcome_unknown", now);
            schedule.MarkOutcomeUnknown(now, "publish_outcome_unknown");
            await PersistAmbiguousOutcomeAsync().ConfigureAwait(false);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _ = exception;
            attempt.MarkOutcomeUnknown(attempt.LeaseToken!.Value, "publish_outcome_unknown", now);
            schedule.MarkOutcomeUnknown(now, "publish_outcome_unknown");
            await PersistAmbiguousOutcomeAsync().ConfigureAwait(false);
            return;
        }

        if (result.Success)
        {
            var externalId = string.IsNullOrWhiteSpace(result.PostUrl)
                ? attempt.IdempotencyKey
                : result.PostUrl!;
            attempt.MarkSucceeded(attempt.LeaseToken!.Value, externalId, now);
            item.MarkPublished(attempt.Id, now);
            schedule.MarkPosted(result.PostUrl ?? string.Empty, now);
            LogPublished(_logger, schedule.TenantId, item.Id, schedule.Id);
            return;
        }

        var reason = string.IsNullOrWhiteSpace(result.Error) ? "publisher_failed" : result.Error;
        if (IsAmbiguousPublishOutcome(reason))
        {
            attempt.MarkOutcomeUnknown(attempt.LeaseToken!.Value, "publish_outcome_unknown", now);
            schedule.MarkOutcomeUnknown(now, "publish_outcome_unknown");
            await PersistAmbiguousOutcomeAsync().ConfigureAwait(false);
            return;
        }

        attempt.MarkFailed(attempt.LeaseToken!.Value, NormalizeAttemptError(reason), now);
        item.ReleasePublishAttempt(attempt.Id, now);
        var willRetry = schedule.RecordRetry(now, reason);
        if (willRetry)
        {
            LogPublishRetrying(_logger, schedule.TenantId, item.Id, schedule.Id, schedule.RetryCount, reason);
            return;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _notifier.NotifyPublishFailedAsync(
            schedule.TenantId,
            new ContentPublishFailedEvent(
                schedule.TenantId,
                item.Id,
                schedule.Id,
                schedule.Platform,
                reason,
                now),
            ct).ConfigureAwait(false);
        LogPublishFailed(_logger, schedule.TenantId, item.Id, schedule.Id, reason);
        await Agents.ScheduleEventDispatcher.FireAsync(
            _db,
            schedule.TenantId,
            Clawbot.SharedKernel.Orchestration.ScheduleEventKeys.ContentPublishFailed,
            now,
            ct).ConfigureAwait(false);

        async Task PersistAmbiguousOutcomeAsync()
        {
            using var saveCts = new CancellationTokenSource(AmbiguousOutcomeSaveTimeout);
            await _db.SaveChangesAsync(saveCts.Token).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ContentPublishAssetSnapshot>> LoadReadyAssetSnapshotsAsync(
        Guid tenantId,
        Guid contentItemId,
        CancellationToken ct)
    {
        var assets = await _db.ContentAssets.IgnoreQueryFilters()
            .Where(asset =>
                asset.TenantId == tenantId
                && asset.ContentItemId == contentItemId
                && asset.Status == ContentAsset.StatusReady)
            .OrderBy(asset => asset.SortOrder)
            .ThenBy(asset => asset.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var snapshots = new List<ContentPublishAssetSnapshot>(assets.Count);
        foreach (var asset in assets)
        {
            var hash = asset.Sha256;
            if (hash is null || hash.Length != 32 || asset.SizeBytes is null or <= 0)
                continue;
            var contentType = string.IsNullOrWhiteSpace(asset.ContentType)
                ? "application/octet-stream"
                : asset.ContentType!;
            snapshots.Add(new ContentPublishAssetSnapshot(
                asset.Id,
                Convert.ToHexString(hash).ToLowerInvariant(),
                asset.SortOrder,
                contentType,
                asset.SizeBytes.Value));
        }

        return snapshots;
    }

    private static string NormalizeAttemptError(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return ContentSchedule.ErrorPublisherFailure;
        var normalized = reason.Trim();
        if (normalized.Length > 128
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or '-' or '.' or ':')))
        {
            return ContentSchedule.ErrorPublisherFailure;
        }

        return normalized;
    }

    private static bool IsAmbiguousPublishOutcome(string reason) =>
        reason is "publisher_timeout" or "facebook_timeout" or "zalo_timeout";

    private static string? ResolvePublishHoldReason(ContentSchedule schedule, ContentItem item)
    {
        if (schedule.ContentRevision != item.ContentRevision)
            return "stale_content_revision";
        if (!item.CanPublishCurrentRevision())
            return "current_revision_not_publishable";
        if (schedule.ApprovalMode is null || schedule.PublishingPolicyVersionApplied is null)
            return "approval_context_missing";
        if (!string.Equals(schedule.ApprovalMode, item.ApprovalMode, StringComparison.Ordinal)
            || schedule.PublishingPolicyVersionApplied != item.PublishingPolicyVersionApplied)
        {
            return "approval_context_mismatch";
        }
        if (InstagramPublishingGate.IsBlocked(schedule.Platform)
            || InstagramPublishingGate.IsBlocked(item.Platform))
        {
            return InstagramPublishingGate.ErrorCode;
        }

        return null;
    }

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Information,
        Message = "Published content item {ContentItemId} for tenant {TenantId} schedule {ScheduleId}")]
    private static partial void LogPublished(ILogger logger, Guid tenantId, Guid contentItemId, Guid scheduleId);

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Warning,
        Message = "Failed publishing content item {ContentItemId} for tenant {TenantId} schedule {ScheduleId}: {Reason}")]
    private static partial void LogPublishFailed(
        ILogger logger,
        Guid tenantId,
        Guid contentItemId,
        Guid scheduleId,
        string reason);

    [LoggerMessage(
        EventId = 5105,
        Level = LogLevel.Warning,
        Message = "Retrying publish for content item {ContentItemId} tenant {TenantId} schedule {ScheduleId} (attempt {Attempt}): {Reason}")]
    private static partial void LogPublishRetrying(
        ILogger logger,
        Guid tenantId,
        Guid contentItemId,
        Guid scheduleId,
        int attempt,
        string reason);

    [LoggerMessage(
        EventId = 5106,
        Level = LogLevel.Warning,
        Message = "Held content item {ContentItemId} tenant {TenantId} schedule {ScheduleId}: tenant requires agent review and item lacks reviewer signoff")]
    private static partial void LogHeldForReview(ILogger logger, Guid tenantId, Guid contentItemId, Guid scheduleId);

    [LoggerMessage(
        EventId = 5107,
        Level = LogLevel.Warning,
        Message = "Canceled stale schedule {ScheduleId} for content item {ContentItemId} tenant {TenantId}: item status is '{Status}', not 'scheduled'")]
    private static partial void LogStaleScheduleSkipped(ILogger logger, Guid tenantId, Guid contentItemId, Guid scheduleId, string status);

    [LoggerMessage(
        EventId = 5108,
        Level = LogLevel.Information,
        Message = "Content publication paused by content_workflow_runtime_gate; skipping due publish batch")]
    private static partial void LogPublicationPaused(ILogger logger);
}
