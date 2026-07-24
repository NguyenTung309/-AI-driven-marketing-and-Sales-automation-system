using System.Text;
using Clawbot.Domain.Common;
using Clawbot.SharedKernel.Content.Visuals;

namespace Clawbot.Domain.Content;

public sealed class ContentRenderTask : Entity<Guid>, ITenantOwned, IAuditExempt
{
    public const string StatusPending = "pending";
    public const string StatusLeased = "leased";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
    public const string StatusCanceledStale = "canceled_stale";

    public const int MaxTemplateIdLength = 64;
    public const int MaxPresetLength = 64;
    public const int MaxCanonicalSlotsUtf8Bytes = ContentVisualLimits.MaximumJsonUtf8Bytes;
    public const int MaxErrorCodeLength = 128;

    private const int Sha256HexLength = 64;

    public Guid TenantId { get; private set; }
    public Guid ContentItemId { get; private set; }
    public int SourceRevision { get; private set; }
    public string TemplateId { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public string TemplateHash { get; private set; } = string.Empty;
    public string Preset { get; private set; } = string.Empty;
    public string CanonicalSlotsJson { get; private set; } = "{}";
    public string SlotsHash { get; private set; } = string.Empty;
    public string Status { get; private set; } = StatusPending;
    public Guid? LeaseToken { get; private set; }
    public Guid? ClaimedLeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public Guid? OutputAssetId { get; private set; }
    public int? CompletedRevision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ContentRenderTask() { }

    public static ContentRenderTask CreatePending(
        Guid tenantId,
        Guid contentItemId,
        int sourceRevision,
        string templateId,
        int templateVersion,
        string templateHash,
        string preset,
        string canonicalSlotsJson,
        string slotsHash,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset createdAt)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(contentItemId, nameof(contentItemId));
        if (sourceRevision <= 0 || sourceRevision == int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceRevision),
                "content_render_task_source_revision_invalid");
        }

        var validatedTemplateId = ValidateTemplateId(templateId);
        var normalizedTemplateVersion = ValidateTemplateVersion(templateVersion);
        var normalizedTemplateHash = NormalizeHash(
            templateHash,
            nameof(templateHash),
            "content_render_task_template_hash_invalid");
        var normalizedPreset = NormalizePreset(preset);
        var (validatedSlots, computedSlotsHash) = CanonicalizeSlots(canonicalSlotsJson);
        var normalizedSlotsHash = NormalizeHash(
            slotsHash,
            nameof(slotsHash),
            "content_render_task_slots_hash_invalid");
        ValidateSlotsHash(computedSlotsHash, normalizedSlotsHash);

        return new ContentRenderTask
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContentItemId = contentItemId,
            SourceRevision = sourceRevision,
            TemplateId = validatedTemplateId,
            TemplateVersion = normalizedTemplateVersion,
            TemplateHash = normalizedTemplateHash,
            Preset = normalizedPreset,
            CanonicalSlotsJson = validatedSlots,
            SlotsHash = normalizedSlotsHash,
            NextAttemptAt = nextAttemptAt,
            CreatedAt = createdAt,
        };
    }

    public void Lease(Guid leaseToken, DateTimeOffset leaseExpiresAt, DateTimeOffset at)
    {
        ValidateIdentity(leaseToken, nameof(leaseToken));
        if (Status != StatusPending)
            throw new InvalidOperationException("content_render_task_not_pending");
        if (NextAttemptAt > at)
            throw new InvalidOperationException("content_render_task_not_due");
        if (leaseExpiresAt <= at)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "content_render_task_lease_expiry_invalid");
        }

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
            throw new InvalidOperationException("content_render_task_not_leased");
        if (LeaseExpiresAt is null || LeaseExpiresAt > at)
            throw new InvalidOperationException("content_render_task_lease_not_expired");
        if (LeaseToken == replacementLeaseToken)
            throw new InvalidOperationException("content_render_task_lease_token_not_rotated");
        if (leaseExpiresAt <= at)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseExpiresAt),
                "content_render_task_lease_expiry_invalid");
        }

        var nextAttemptCount = checked(AttemptCount + 1);
        LeaseToken = replacementLeaseToken;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = leaseExpiresAt;
        AttemptCount = nextAttemptCount;
        LastErrorCode = "lease_expired";
    }

    public bool TryClaimDelivery(Guid leaseToken, DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        if (ClaimedLeaseToken == leaseToken)
            return false;
        if (ClaimedLeaseToken is not null)
            throw new InvalidOperationException("content_render_task_claim_mismatch");

        ClaimedLeaseToken = leaseToken;
        return true;
    }

    public void ReleaseForRetry(
        Guid leaseToken,
        DateTimeOffset nextAttemptAt,
        string errorCode,
        DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        if (nextAttemptAt < at)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextAttemptAt),
                "content_render_task_retry_time_invalid");
        }

        var normalizedError = NormalizeErrorCode(errorCode);
        Status = StatusPending;
        LeaseToken = null;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = null;
        NextAttemptAt = nextAttemptAt;
        LastErrorCode = normalizedError;
    }

    public void Complete(
        Guid leaseToken,
        Guid outputAssetId,
        int completedRevision,
        DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        ValidateIdentity(outputAssetId, nameof(outputAssetId));
        if ((long)completedRevision != (long)SourceRevision + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedRevision),
                "content_render_task_completed_revision_invalid");
        }

        Status = StatusCompleted;
        OutputAssetId = outputAssetId;
        CompletedRevision = completedRevision;
        CompletedAt = at;
        ClearLease();
        LastErrorCode = null;
    }

    public void Fail(Guid leaseToken, string errorCode, DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        var normalizedError = NormalizeErrorCode(errorCode);

        Status = StatusFailed;
        CompletedAt = at;
        ClearLease();
        LastErrorCode = normalizedError;
    }

    public void FailExhausted(int maxAttempts, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        EnsureNotTerminal();
        if (AttemptCount < maxAttempts)
            throw new InvalidOperationException("content_render_task_attempt_limit_not_reached");
        if (Status == StatusLeased
            && LeaseExpiresAt is not null
            && LeaseExpiresAt > at)
        {
            throw new InvalidOperationException("content_render_task_lease_active");
        }

        Status = StatusFailed;
        CompletedAt = at;
        ClearLease();
        LastErrorCode = "content_render_attempt_limit_reached";
    }

    public void CancelStale(DateTimeOffset at)
    {
        EnsureNotTerminal();

        Status = StatusCanceledStale;
        CompletedAt = at;
        ClearLease();
        LastErrorCode = "stale_content_revision";
    }

    private void EnsureActiveLease(Guid leaseToken, DateTimeOffset at)
    {
        if (Status != StatusLeased)
            throw new InvalidOperationException("content_render_task_not_leased");
        if (LeaseToken != leaseToken)
            throw new InvalidOperationException("content_render_task_lease_mismatch");
        if (LeaseExpiresAt is null || LeaseExpiresAt <= at)
            throw new InvalidOperationException("content_render_task_lease_expired");
    }

    private void EnsureNotTerminal()
    {
        if (Status is StatusCompleted or StatusFailed or StatusCanceledStale)
            throw new InvalidOperationException("content_render_task_terminal");
    }

    private void ClearLease()
    {
        LeaseToken = null;
        ClaimedLeaseToken = null;
        LeaseExpiresAt = null;
    }

    private static string ValidateTemplateId(string templateId)
    {
        if (string.IsNullOrEmpty(templateId) || templateId.Length > MaxTemplateIdLength)
            throw new ArgumentException("content_render_task_template_id_invalid", nameof(templateId));

        for (var index = 0; index < templateId.Length; index++)
        {
            var character = templateId[index];
            var isAlphaNumeric = character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9';
            var isSeparator = index > 0 && character is '-' or '_' or '.';
            if (!isAlphaNumeric && !isSeparator)
                throw new ArgumentException("content_render_task_template_id_invalid", nameof(templateId));
        }

        return templateId;
    }

    private static int ValidateTemplateVersion(int templateVersion)
    {
        if (templateVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(templateVersion),
                "content_render_task_template_version_invalid");
        }

        return templateVersion;
    }

    private static string NormalizePreset(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            throw new ArgumentException("content_render_task_preset_required", nameof(preset));

        if (preset.Length > MaxPresetLength)
            throw new ArgumentException("content_render_task_preset_invalid", nameof(preset));

        try
        {
            return ContentVisualPreset.Parse(preset).Token;
        }
        catch (ContentVisualContractException exception)
        {
            throw new ArgumentException(
                "content_render_task_preset_invalid",
                nameof(preset),
                exception);
        }
    }

    private static (string Json, string Hash) CanonicalizeSlots(string canonicalSlotsJson)
    {
        if (string.IsNullOrWhiteSpace(canonicalSlotsJson))
        {
            throw new ArgumentException(
                "content_render_task_slots_required",
                nameof(canonicalSlotsJson));
        }

        if (Encoding.UTF8.GetByteCount(canonicalSlotsJson) > MaxCanonicalSlotsUtf8Bytes)
        {
            throw new ArgumentException(
                "content_render_task_slots_invalid",
                nameof(canonicalSlotsJson));
        }

        try
        {
            var slots = ContentRenderSpecJson.ParseSlots(canonicalSlotsJson);
            return (
                ContentRenderSpecCanonicalizer.ToCanonicalSlotsJson(slots),
                ContentRenderSpecCanonicalizer.ComputeSlotsSha256(slots));
        }
        catch (ContentVisualContractException exception)
        {
            throw new ArgumentException(
                "content_render_task_slots_invalid",
                nameof(canonicalSlotsJson),
                exception);
        }
    }

    private static void ValidateSlotsHash(string computedHash, string slotsHash)
    {
        if (!string.Equals(computedHash, slotsHash, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "content_render_task_slots_hash_mismatch",
                nameof(slotsHash));
        }
    }

    private static string NormalizeHash(
        string hash,
        string parameterName,
        string errorCode)
    {
        if (hash is null
            || hash.Length != Sha256HexLength
            || hash.Any(character => !Uri.IsHexDigit(character)
                || character is >= 'A' and <= 'F'))
        {
            throw new ArgumentException(errorCode, parameterName);
        }

        return hash;
    }

    private static string NormalizeErrorCode(string errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException(
                "content_render_task_error_code_required",
                nameof(errorCode));
        }

        var normalized = errorCode.Trim();
        if (normalized.Length > MaxErrorCodeLength || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or '-' or '.' or ':')))
        {
            throw new ArgumentException(
                "content_render_task_error_code_invalid",
                nameof(errorCode));
        }

        return normalized.ToLowerInvariant();
    }

    private static void ValidateIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("content_render_task_identity_required", parameterName);
    }
}
