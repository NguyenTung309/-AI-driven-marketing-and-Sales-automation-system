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
            // Cách ly từng schedule: 1 row lỗi bất ngờ không được giết cả batch (và gây bão retry Hangfire).
            try
            {
                await ProcessDueScheduleAsync(schedule, itemsById, now, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                DiscardPendingChanges();
                LogScheduleProcessingFailed(_logger, schedule.TenantId, schedule.ContentItemId, schedule.Id, exception);
            }
        }
    }

    private async Task ProcessDueScheduleAsync(
        ContentSchedule schedule,
        Dictionary<Guid, ContentItem> itemsById,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!itemsById.TryGetValue(schedule.ContentItemId, out var item) || item.TenantId != schedule.TenantId)
        {
            // Item missing / hard-deleted — free the unique pending index so user can re-schedule.
            schedule.Cancel(now, "item_missing");
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        // Stale schedule: item was reverted/rejected after scheduling — cancel so it no longer blocks
        // the unique pending index and calendar shows a terminal state with reason.
        if (!string.Equals(item.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            var staleReason = ContentSchedule.ErrorStaleItemPrefix + item.Status;
            schedule.Cancel(now, staleReason);
            LogStaleScheduleSkipped(_logger, schedule.TenantId, item.Id, schedule.Id, item.Status);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        if (schedule.ContentRevision != item.ContentRevision)
        {
            schedule.Cancel(now, "stale_content_revision");
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        await AttemptPublishAsync(schedule, item, now, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // Hoàn tác các thay đổi đang chờ của schedule vừa lỗi để vòng lặp chạy tiếp an toàn,
    // giữ nguyên các entity đã lưu thành công (Unchanged) ở các lượt trước.
    private void DiscardPendingChanges()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
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

        if (schedule.RequiresInstagramTargetReselection())
            return (false, ContentSchedule.ErrorInstagramTargetReselectionRequired);

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
        var leaseExpiresAt = now.AddMinutes(10);

        // UX_content_publish_attempts_operation cho đúng 1 row cho mỗi
        // (tenant, schedule, item, revision, target). Lần thử lại phải TÁI DÙNG row đó — claim
        // row mới sẽ đụng unique index (Msg 2601) và giết cả batch. Lần đầu mới insert.
        // IgnoreQueryFilters BẮT BUỘC: job scope Hangfire không có tenant context nên query
        // filter theo tenant sẽ cắt kết quả về rỗng, khiến lookup luôn tưởng chưa có attempt.
        var existingAttempt = await _db.ContentPublishAttempts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == schedule.TenantId
                    && candidate.ScheduleId == schedule.Id
                    && candidate.ContentItemId == item.Id
                    && candidate.ContentRevision == item.ContentRevision
                    && candidate.PublishTargetId == publishTargetId,
                ct)
            .ConfigureAwait(false);

        if (existingAttempt is not null)
        {
            // Bài đã thực sự lên provider ở lần trước — tuyệt đối không gọi lại (chống đăng trùng).
            if (existingAttempt.HasConfirmedPublication())
            {
                ReconcileConfirmedPublication(schedule, item, existingAttempt, now);
                LogPublished(_logger, schedule.TenantId, item.Id, schedule.Id);
                return;
            }

            // Một tiến trình khác đang giữ lease còn hạn — nhường nó, thử lại lượt sau.
            if (existingAttempt.HasActiveLease(now))
                return;

            // transmitted hết lease / outcome_unknown: có thể đã đăng, thuộc về reconciliation job.
            if (!existingAttempt.CanReopenForRetry())
            {
                schedule.MarkHeld("publish_attempt_awaiting_reconciliation", now);
                return;
            }
        }

        ContentPublishAttempt attempt;
        if (existingAttempt is null)
        {
            // Snapshot bất biến chỉ chốt một lần khi claim; reopen tái dùng nên không cần load lại.
            var assetSnapshots = await LoadReadyAssetSnapshotsAsync(
                schedule.TenantId,
                item.Id,
                ct).ConfigureAwait(false);
            attempt = ContentPublishAttempt.Claim(
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
                attemptSequence: 1);
            _db.ContentPublishAttempts.Add(attempt);
        }
        else
        {
            attempt = existingAttempt;
            attempt.ReopenForRetry(Guid.NewGuid(), leaseExpiresAt, now);
        }

        item.ClaimPublishAttempt(item.ContentRevision, attempt.Id, now);
        schedule.MarkPublishing(now);
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
            schedule.MetaAssetId,
            schedule.ProviderTargetId);
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
            var externalId = !string.IsNullOrWhiteSpace(result.ExternalId)
                ? result.ExternalId!
                : !string.IsNullOrWhiteSpace(result.PostUrl)
                    ? result.PostUrl!
                    : attempt.IdempotencyKey;
            attempt.MarkSucceeded(attempt.LeaseToken!.Value, externalId, now);
            item.MarkPublished(attempt.Id, now);
            schedule.MarkPosted(result.PostUrl ?? string.Empty, now);
            LogPublished(_logger, schedule.TenantId, item.Id, schedule.Id);
            return;
        }

        var reason = string.IsNullOrWhiteSpace(result.Error) ? "publisher_failed" : result.Error;
        if (IsAmbiguousPublishOutcome(reason))
        {
            var outcomeError = NormalizeAttemptError(reason);
            attempt.MarkOutcomeUnknown(attempt.LeaseToken!.Value, outcomeError, now);
            schedule.MarkOutcomeUnknown(now, outcomeError);
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

    // Attempt trước đã xác nhận lên provider nhưng schedule bị đưa lại 'pending' (vd. thao tác thử lại
    // tay). Đồng bộ trạng thái cuối mà KHÔNG gọi provider lần nữa, tránh đăng trùng.
    private static void ReconcileConfirmedPublication(
        ContentSchedule schedule,
        ContentItem item,
        ContentPublishAttempt attempt,
        DateTimeOffset now)
    {
        if (!string.Equals(schedule.Status, ContentSchedule.StatusPosted, StringComparison.Ordinal))
        {
            schedule.MarkPublishing(now);
            schedule.MarkPosted(attempt.ExternalPostId ?? string.Empty, now);
        }

        // Chỉ chạm item khi nó vẫn ở 'scheduled' và hợp lệ; các trạng thái khác đã terminal.
        if (item.CanPublishCurrentRevision())
        {
            item.ClaimPublishAttempt(item.ContentRevision, attempt.Id, now);
            item.MarkPublished(attempt.Id, now);
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
        reason is "publish_outcome_unknown"
            or "facebook_timeout"
            or "facebook_unavailable"
            or "facebook_response_invalid"
            or "facebook_response_missing_id"
            or "instagram_timeout"
            or "instagram_unavailable"
            or "instagram_error"
        || reason.StartsWith("facebook_outcome_unknown:", StringComparison.Ordinal)
        || reason.StartsWith("instagram_outcome_unknown:", StringComparison.Ordinal)
        || reason.StartsWith("zalo_outcome_unknown:", StringComparison.Ordinal)
        || reason.StartsWith("facebook_graph_", StringComparison.Ordinal)
        || reason.StartsWith("instagram_graph_", StringComparison.Ordinal)
        || IsLegacyAmbiguousFacebookHttpError(reason);

    private static bool IsLegacyAmbiguousFacebookHttpError(string reason)
    {
        const string prefix = "facebook_http_";
        return reason.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                reason.AsSpan(prefix.Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var statusCode)
            && (statusCode is 408 or 429 || statusCode >= 500);
    }

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
        if (schedule.RequiresInstagramTargetReselection())
            return ContentSchedule.ErrorInstagramTargetReselectionRequired;
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

    [LoggerMessage(
        EventId = 5109,
        Level = LogLevel.Error,
        Message = "Failed processing due schedule {ScheduleId} for content item {ContentItemId} tenant {TenantId}; skipping to keep batch alive")]
    private static partial void LogScheduleProcessingFailed(
        ILogger logger,
        Guid tenantId,
        Guid contentItemId,
        Guid scheduleId,
        Exception exception);
}
