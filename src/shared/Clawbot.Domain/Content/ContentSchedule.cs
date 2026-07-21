using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentSchedule : AggregateRoot<Guid>, ITenantOwned
{
    public const int MaxRetries = 3;
    public const int MaxLastErrorLength = 1024;

    public const string StatusPending = "pending";
    public const string StatusHeld = "held";
    public const string StatusPublishing = "publishing";
    public const string StatusOutcomeUnknown = "outcome_unknown";
    public const string StatusPosted = "posted";
    public const string StatusFailed = "failed";
    public const string StatusCanceled = "canceled";

    public const string ErrorHeldForReview = "held_for_review";
    public const string ErrorCanceledByUser = "canceled_by_user";
    public const string ErrorPublisherFailure = "publisher_error";
    public const string ErrorStaleItemPrefix = "stale_item_status:";

    private const int MaxErrorCodeLength = 128;

    public Guid TenantId { get; private set; }
    public Guid ContentItemId { get; private set; }
    public int? ContentRevision { get; private set; }
    public int? ActiveRevisionSlot { get; private set; }
    public Guid? MetaAssetId { get; private set; }
    public Guid? PublishTargetId { get; private set; }
    public string? ApprovalMode { get; private set; }
    public long? PublishingPolicyVersionApplied { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public string? PostUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    // Last publisher/job reason (retry, fail, hold, stale). Null when healthy pending or successfully posted.
    public string? LastError { get; private set; }
    // Engagement counts fetched back from the platform (FB Graph) after publishing. Null = not synced yet.
    public int? LikeCount { get; private set; }
    public int? CommentCount { get; private set; }
    public DateTimeOffset? EngagementSyncedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ContentSchedule() { }

    public static ContentSchedule Schedule(
        Guid tenantId,
        Guid contentItemId,
        int contentRevision,
        string platform,
        DateTimeOffset scheduledAt,
        DateTimeOffset createdAt,
        Guid? metaAssetId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentRevision);
        return new ContentSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContentItemId = contentItemId,
            ContentRevision = contentRevision,
            ActiveRevisionSlot = contentRevision,
            MetaAssetId = metaAssetId,
            PublishTargetId = metaAssetId,
            Platform = platform,
            ScheduledAt = scheduledAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public void SetApprovalContext(
        string approvalMode,
        long publishingPolicyVersionApplied,
        Guid? publishTargetId)
    {
        if (Status != StatusPending)
            throw new InvalidOperationException("content_schedule_approval_context_requires_pending");
        if (ApprovalMode is not null || PublishingPolicyVersionApplied is not null)
            throw new InvalidOperationException("content_schedule_approval_context_already_set");
        if (approvalMode is not (ContentItem.ApprovalModeAutomatic
            or ContentItem.ApprovalModeHuman
            or ContentItem.ApprovalModeHumanOverride))
        {
            throw new ArgumentException("content_schedule_approval_mode_invalid", nameof(approvalMode));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(publishingPolicyVersionApplied);

        ApprovalMode = approvalMode;
        PublishingPolicyVersionApplied = publishingPolicyVersionApplied;
        PublishTargetId = publishTargetId;
    }

    public void MarkPublishing(DateTimeOffset at)
    {
        if (Status != StatusPending)
            throw new InvalidOperationException("content_schedule_not_pending");
        Status = StatusPublishing;
        ActiveRevisionSlot = ContentRevision;
        NextAttemptAt = null;
        LastError = null;
        LastErrorCode = null;
        UpdatedAt = at;
    }

    public void MarkPosted(string postUrl, DateTimeOffset at)
    {
        if (Status != StatusPublishing)
            throw new InvalidOperationException("content_schedule_not_publishing");
        Status = StatusPosted;
        ActiveRevisionSlot = null;
        PostedAt = at;
        PostUrl = postUrl;
        NextAttemptAt = null;
        LastError = null;
        LastErrorCode = null;
        UpdatedAt = at;
    }

    public void MarkOutcomeUnknown(DateTimeOffset at, string? reason = null)
    {
        if (Status != StatusPublishing)
            throw new InvalidOperationException("content_schedule_not_publishing");
        Status = StatusOutcomeUnknown;
        ActiveRevisionSlot = ContentRevision;
        LastErrorCode = NormalizeError(reason) ?? "publish_outcome_unknown";
        LastError = LastErrorCode;
        NextAttemptAt = null;
        UpdatedAt = at;
    }

    public void MarkFailed(DateTimeOffset at, string? reason = null)
    {
        if (Status is StatusPosted or StatusCanceled or StatusOutcomeUnknown)
            throw new InvalidOperationException("content_schedule_terminal");
        Status = StatusFailed;
        ActiveRevisionSlot = null;
        LastErrorCode = NormalizeError(reason) ?? LastErrorCode;
        LastError = LastErrorCode ?? LastError;
        NextAttemptAt = null;
        UpdatedAt = at;
    }

    public bool RecordRetry(DateTimeOffset at, string? reason = null)
    {
        if (Status is StatusPosted or StatusCanceled or StatusOutcomeUnknown)
            throw new InvalidOperationException("content_schedule_terminal");
        RetryCount = checked(RetryCount + 1);
        LastErrorCode = NormalizeError(reason) ?? LastErrorCode;
        LastError = LastErrorCode ?? LastError;
        UpdatedAt = at;
        if (RetryCount >= MaxRetries)
        {
            Status = StatusFailed;
            ActiveRevisionSlot = null;
            NextAttemptAt = null;
            return false;
        }

        Status = StatusPending;
        ActiveRevisionSlot = ContentRevision;
        return true;
    }

    public void MarkHeld(string reason, DateTimeOffset at, DateTimeOffset? nextAttemptAt = null)
    {
        if (Status is not (StatusPending or StatusHeld))
            throw new InvalidOperationException("content_schedule_cannot_be_held");
        Status = StatusHeld;
        ActiveRevisionSlot = ContentRevision;
        LastErrorCode = NormalizeError(reason) ?? ErrorHeldForReview;
        LastError = LastErrorCode;
        NextAttemptAt = nextAttemptAt;
        UpdatedAt = at;
    }

    public void Cancel(DateTimeOffset at, string? reason = null)
    {
        if (Status is not (StatusPending or StatusHeld or StatusFailed))
            throw new InvalidOperationException("content_schedule_cannot_be_canceled");
        Status = StatusCanceled;
        ActiveRevisionSlot = null;
        LastErrorCode = NormalizeError(reason) ?? ErrorCanceledByUser;
        LastError = LastErrorCode;
        NextAttemptAt = null;
        UpdatedAt = at;
    }

    // Manual "Đăng ngay / Thử lại": failed or stuck pending → clean pending for another attempt.
    public bool TryResetForRetry(DateTimeOffset at)
    {
        if (!string.Equals(Status, StatusFailed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Status, StatusHeld, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Status, StatusPending, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Status = StatusPending;
        ActiveRevisionSlot = ContentRevision;
        RetryCount = 0;
        NextAttemptAt = null;
        LastErrorCode = null;
        LastError = null;
        UpdatedAt = at;
        return true;
    }

    // Phase 4.6: privileged reconcile after operator verification — never calls provider.
    public void MarkReconciledPosted(string postUrl, DateTimeOffset at)
    {
        if (Status != StatusOutcomeUnknown)
            throw new InvalidOperationException("content_schedule_not_outcome_unknown");
        Status = StatusPosted;
        ActiveRevisionSlot = null;
        PostedAt = at;
        PostUrl = postUrl;
        NextAttemptAt = null;
        LastError = null;
        LastErrorCode = null;
        UpdatedAt = at;
    }

    public void MarkReconciledFailed(DateTimeOffset at, string? reason = null)
    {
        if (Status != StatusOutcomeUnknown)
            throw new InvalidOperationException("content_schedule_not_outcome_unknown");
        Status = StatusFailed;
        ActiveRevisionSlot = null;
        LastErrorCode = NormalizeError(reason) ?? "publish_reconciled_failed";
        LastError = LastErrorCode;
        NextAttemptAt = null;
        UpdatedAt = at;
    }

    public void SetEngagement(int? likeCount, int? commentCount, DateTimeOffset at)
    {
        LikeCount = likeCount;
        CommentCount = commentCount;
        EngagementSyncedAt = at;
        UpdatedAt = at;
    }

    private static string? NormalizeError(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;
        var normalized = reason.Trim();
        if (normalized.Length > MaxErrorCodeLength
            || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or '-' or '.' or ':')))
        {
            return ErrorPublisherFailure;
        }

        return normalized;
    }
}
