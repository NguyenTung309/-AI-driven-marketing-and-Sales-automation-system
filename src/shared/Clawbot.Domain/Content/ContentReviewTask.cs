using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentReviewTask : Entity<Guid>, ITenantOwned, IAuditExempt
{
    public const string StatusPending = "pending";
    public const string StatusLeased = "leased";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
    public const string StatusCanceledStale = "canceled_stale";

    private const int MaxErrorCodeLength = 128;

    public Guid TenantId { get; private set; }
    public Guid ContentItemId { get; private set; }
    public int ContentRevision { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public Guid? LeaseToken { get; private set; }
    public Guid? ClaimedLeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public int AttemptCount { get; private set; }
    // Refine (P6, §4.7): số vòng sửa tự động đã chạy cho revision này. Đúng 1 vòng/revision — reviewer reject lần
    // đầu (RefineAttemptCount==0) mới kích refine; vòng 2 vẫn reject => needs_human, dừng hẳn. Đếm trên task (bền),
    // KHÔNG trong bộ nhớ tiến trình, để restart/đa host không chạy lại vòng đã dùng.
    public int RefineAttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ContentReviewTask() { }

    public static ContentReviewTask CreatePending(
        Guid tenantId,
        Guid contentItemId,
        int contentRevision,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset createdAt)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(contentItemId, nameof(contentItemId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentRevision);

        return new ContentReviewTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContentItemId = contentItemId,
            ContentRevision = contentRevision,
            NextAttemptAt = nextAttemptAt,
            CreatedAt = createdAt,
        };
    }

    public void Lease(Guid leaseToken, DateTimeOffset leaseExpiresAt, DateTimeOffset at)
    {
        ValidateIdentity(leaseToken, nameof(leaseToken));
        if (Status != StatusPending)
            throw new InvalidOperationException("content_review_task_not_pending");
        if (NextAttemptAt > at)
            throw new InvalidOperationException("content_review_task_not_due");
        if (leaseExpiresAt <= at)
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt), "content_review_task_lease_expiry_invalid");

        var nextAttemptCount = checked(AttemptCount + 1);
        Status = StatusLeased;
        LeaseToken = leaseToken;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = leaseExpiresAt;
        AttemptCount = nextAttemptCount;
        StartedAt ??= at;
        LastErrorCode = null;
    }

    public void ReclaimExpiredLease(
        Guid replacementLeaseToken,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset at)
    {
        ValidateIdentity(replacementLeaseToken, nameof(replacementLeaseToken));
        if (Status != StatusLeased)
            throw new InvalidOperationException("content_review_task_not_leased");
        if (LeaseExpiresAt is null || LeaseExpiresAt > at)
            throw new InvalidOperationException("content_review_task_lease_not_expired");
        if (LeaseToken == replacementLeaseToken)
            throw new InvalidOperationException("content_review_task_lease_token_not_rotated");
        if (leaseExpiresAt <= at)
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt), "content_review_task_lease_expiry_invalid");

        LeaseToken = replacementLeaseToken;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = leaseExpiresAt;
        AttemptCount = checked(AttemptCount + 1);
        LastErrorCode = "lease_expired";
    }

    public bool TryClaimDelivery(Guid leaseToken, DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        if (ClaimedLeaseToken == leaseToken)
            return false;
        if (ClaimedLeaseToken is not null)
            throw new InvalidOperationException("content_review_task_claim_mismatch");

        ClaimedLeaseToken = leaseToken;
        return true;
    }

    // Refine (P6, §4.7): đánh dấu đã chạy 1 vòng sửa cho lần review này. Đếm trên task (bền qua tiến trình) —
    // chỉ RefineAttemptCount==0 mới được kích refine; gọi lần 2 ném lỗi (vòng 2 reject => needs_human, không refine tiếp).
    public void RecordRefineAttempt(Guid leaseToken, DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        if (RefineAttemptCount > 0)
            throw new InvalidOperationException("content_review_task_refine_exhausted");
        RefineAttemptCount = checked(RefineAttemptCount + 1);
    }

    public void ReleaseForRetry(
        Guid leaseToken,
        DateTimeOffset nextAttemptAt,
        string errorCode,
        DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        if (nextAttemptAt < at)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt), "content_review_task_retry_time_invalid");
        var normalizedError = NormalizeErrorCode(errorCode);

        Status = StatusPending;
        LeaseToken = null;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = null;
        NextAttemptAt = nextAttemptAt;
        LastErrorCode = normalizedError;
    }

    public void Complete(Guid leaseToken, DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        Status = StatusCompleted;
        CompletedAt = at;
        LeaseToken = null;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = null;
        LastErrorCode = null;
    }

    public void Fail(Guid leaseToken, string errorCode, DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        var normalizedError = NormalizeErrorCode(errorCode);

        Status = StatusFailed;
        CompletedAt = at;
        LeaseToken = null;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = null;
        LastErrorCode = normalizedError;
    }

    // Terminalizes a pending or expired leased task once attempt_count has already hit the
    // shared review cap. Must not steal an active unexpired lease from another owner.
    public void FailExhausted(int maxAttempts, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        if (Status is StatusCompleted or StatusFailed or StatusCanceledStale)
            throw new InvalidOperationException("content_review_task_terminal");
        if (AttemptCount < maxAttempts)
            throw new InvalidOperationException("content_review_task_attempt_limit_not_reached");
        if (Status == StatusLeased
            && LeaseExpiresAt is not null
            && LeaseExpiresAt > at)
        {
            throw new InvalidOperationException("content_review_task_lease_active");
        }

        Status = StatusFailed;
        CompletedAt = at;
        LeaseToken = null;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = null;
        LastErrorCode = "content_review_attempt_limit_reached";
    }

    public void CancelStale(DateTimeOffset at)
    {
        if (Status is StatusCompleted or StatusFailed or StatusCanceledStale)
            throw new InvalidOperationException("content_review_task_terminal");

        Status = StatusCanceledStale;
        CompletedAt = at;
        LeaseToken = null;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = null;
        LastErrorCode = "stale_content_revision";
    }

    private void EnsureActiveLease(Guid leaseToken, DateTimeOffset at)
    {
        if (Status != StatusLeased)
            throw new InvalidOperationException("content_review_task_not_leased");
        if (LeaseToken != leaseToken)
            throw new InvalidOperationException("content_review_task_lease_mismatch");
        if (LeaseExpiresAt is null || LeaseExpiresAt <= at)
            throw new InvalidOperationException("content_review_task_lease_expired");
    }

    private static string NormalizeErrorCode(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("content_review_task_error_code_required", nameof(errorCode));
        var normalized = errorCode.Trim();
        if (normalized.Length > MaxErrorCodeLength || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or '-' or '.' or ':')))
        {
            throw new ArgumentException("content_review_task_error_code_invalid", nameof(errorCode));
        }

        return normalized;
    }

    private static void ValidateIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("content_review_task_identity_required", parameterName);
    }
}
