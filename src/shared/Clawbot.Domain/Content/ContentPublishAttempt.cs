using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentPublishAttempt : Entity<Guid>, ITenantOwned, IAuditExempt
{
    public const string StatusClaimed = "claimed";
    public const string StatusTransmitted = "transmitted";
    public const string StatusSucceeded = "succeeded";
    public const string StatusFailed = "failed";
    public const string StatusOutcomeUnknown = "outcome_unknown";
    public const string StatusReconciled = "reconciled";

    public const int CurrentSnapshotSchemaVersion = 1;

    private const int MaxPlatformLength = 32;
    private const int MaxProviderIdentifierLength = 256;
    private const int MaxErrorCodeLength = 128;
    private const int MaxAssetCount = 20;
    private const int Sha256HexLength = 64;

    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        new(JsonSerializerDefaults.Web);

    private byte[] _snapshotSha256 = [];

    public Guid TenantId { get; private set; }
    public Guid ScheduleId { get; private set; }
    public Guid ContentItemId { get; private set; }
    public int ContentRevision { get; private set; }
    public Guid PublishTargetId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public Guid AttemptToken { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int SnapshotSchemaVersion { get; private set; }
    public string BodySnapshot { get; private set; } = string.Empty;
    public string AssetsSnapshotJson { get; private set; } = "[]";
    public byte[] SnapshotSha256 => _snapshotSha256.ToArray();
    public string Status { get; private set; } = StatusClaimed;
    public string? ProviderRequestId { get; private set; }
    public string? ExternalPostId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset? TransmittedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ContentPublishAttempt() { }

    public static ContentPublishAttempt Claim(
        Guid tenantId,
        Guid scheduleId,
        Guid contentItemId,
        int contentRevision,
        string platform,
        Guid publishTargetId,
        string bodySnapshot,
        IReadOnlyList<ContentPublishAssetSnapshot> assetSnapshots,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset claimedAt,
        int attemptSequence = 1)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(scheduleId, nameof(scheduleId));
        ValidateIdentity(contentItemId, nameof(contentItemId));
        ValidateIdentity(publishTargetId, nameof(publishTargetId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentRevision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attemptSequence);
        if (leaseExpiresAt <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt), "content_publish_attempt_lease_expiry_invalid");
        var normalizedPlatform = NormalizeRequired(
            platform,
            MaxPlatformLength,
            "content_publish_attempt_platform_required").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(bodySnapshot))
            throw new ArgumentException("content_publish_attempt_body_required", nameof(bodySnapshot));
        var assetsSnapshotJson = BuildAssetsSnapshot(assetSnapshots);

        var id = Guid.NewGuid();
        var attemptToken = Guid.NewGuid();
        // Sequence differentiates definitive-failure retries; same sequence reuses stable provider key.
        var idempotencyKey = BuildIdempotencyKey(
            tenantId,
            scheduleId,
            contentRevision,
            publishTargetId,
            attemptSequence);
        var snapshotHash = ComputeSnapshotHash(
            tenantId,
            scheduleId,
            contentItemId,
            contentRevision,
            normalizedPlatform,
            publishTargetId,
            bodySnapshot,
            assetsSnapshotJson);

        return new ContentPublishAttempt
        {
            Id = id,
            TenantId = tenantId,
            ScheduleId = scheduleId,
            ContentItemId = contentItemId,
            ContentRevision = contentRevision,
            PublishTargetId = publishTargetId,
            Platform = normalizedPlatform,
            AttemptToken = attemptToken,
            LeaseToken = attemptToken,
            LeaseExpiresAt = leaseExpiresAt,
            IdempotencyKey = idempotencyKey,
            SnapshotSchemaVersion = CurrentSnapshotSchemaVersion,
            BodySnapshot = bodySnapshot,
            AssetsSnapshotJson = assetsSnapshotJson,
            _snapshotSha256 = snapshotHash,
            ClaimedAt = claimedAt,
        };
    }

    public void ReclaimExpiredClaim(
        Guid replacementLeaseToken,
        DateTimeOffset leaseExpiresAt,
        DateTimeOffset at)
    {
        ValidateIdentity(replacementLeaseToken, nameof(replacementLeaseToken));
        if (Status != StatusClaimed)
            throw new InvalidOperationException("content_publish_attempt_not_claimed");
        if (LeaseExpiresAt is null || LeaseExpiresAt > at)
            throw new InvalidOperationException("content_publish_attempt_lease_not_expired");
        if (LeaseToken == replacementLeaseToken)
            throw new InvalidOperationException("content_publish_attempt_lease_token_not_rotated");
        if (leaseExpiresAt <= at)
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt), "content_publish_attempt_lease_expiry_invalid");

        LeaseToken = replacementLeaseToken;
        LeaseExpiresAt = leaseExpiresAt;
        LastErrorCode = "lease_expired";
    }

    public void MarkTransmitted(
        Guid leaseToken,
        string? providerRequestId,
        DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        if (Status != StatusClaimed)
            throw new InvalidOperationException("content_publish_attempt_not_claimed");

        Status = StatusTransmitted;
        ProviderRequestId = NormalizeOptional(providerRequestId, MaxProviderIdentifierLength);
        TransmittedAt = at;
    }

    public void MarkSucceeded(
        Guid leaseToken,
        string externalPostId,
        DateTimeOffset at)
    {
        EnsureActiveLease(leaseToken, at);
        if (Status is not (StatusClaimed or StatusTransmitted))
            throw new InvalidOperationException("content_publish_attempt_not_active");
        var normalizedExternalId = NormalizeRequired(
            externalPostId,
            MaxProviderIdentifierLength,
            "content_publish_attempt_external_id_required");

        Status = StatusSucceeded;
        ExternalPostId = normalizedExternalId;
        CompletedAt = at;
        LastErrorCode = null;
        ClearLease();
    }

    public void MarkFailed(Guid leaseToken, string errorCode, DateTimeOffset at)
    {
        if (Status is not (StatusClaimed or StatusTransmitted))
            throw new InvalidOperationException("content_publish_attempt_not_active");
        EnsureActiveLease(leaseToken, at);
        var normalizedError = NormalizeRequired(
            errorCode,
            MaxErrorCodeLength,
            "content_publish_attempt_error_code_required");

        Status = StatusFailed;
        CompletedAt = at;
        LastErrorCode = normalizedError;
        ClearLease();
    }

    public void MarkOutcomeUnknown(Guid leaseToken, string errorCode, DateTimeOffset at)
    {
        if (Status != StatusTransmitted)
            throw new InvalidOperationException("content_publish_attempt_not_transmitted");
        EnsureActiveLease(leaseToken, at);
        TransitionToOutcomeUnknown(errorCode, at);
    }

    public void MarkExpiredTransmissionOutcomeUnknown(string errorCode, DateTimeOffset at)
    {
        if (Status != StatusTransmitted)
            throw new InvalidOperationException("content_publish_attempt_not_transmitted");
        if (LeaseExpiresAt is null || LeaseExpiresAt > at)
            throw new InvalidOperationException("content_publish_attempt_lease_not_expired");

        TransitionToOutcomeUnknown(errorCode, at);
    }

    public void ReconcileSucceeded(string externalPostId, DateTimeOffset at)
    {
        EnsureOutcomeUnknown();
        ExternalPostId = NormalizeRequired(
            externalPostId,
            MaxProviderIdentifierLength,
            "content_publish_attempt_external_id_required");
        Status = StatusReconciled;
        CompletedAt = at;
        LastErrorCode = null;
    }

    public void ReconcileFailed(string errorCode, DateTimeOffset at)
    {
        EnsureOutcomeUnknown();
        Status = StatusReconciled;
        CompletedAt = at;
        LastErrorCode = NormalizeRequired(
            errorCode,
            MaxErrorCodeLength,
            "content_publish_attempt_error_code_required");
    }

    private void TransitionToOutcomeUnknown(string errorCode, DateTimeOffset at)
    {
        var normalizedError = NormalizeRequired(
            errorCode,
            MaxErrorCodeLength,
            "content_publish_attempt_error_code_required");
        Status = StatusOutcomeUnknown;
        CompletedAt = at;
        LastErrorCode = normalizedError;
        ClearLease();
    }

    private void EnsureOutcomeUnknown()
    {
        if (Status != StatusOutcomeUnknown)
            throw new InvalidOperationException("content_publish_attempt_not_outcome_unknown");
    }

    private void EnsureActiveLease(Guid leaseToken, DateTimeOffset at)
    {
        if (LeaseToken != leaseToken)
            throw new InvalidOperationException("content_publish_attempt_token_mismatch");
        if (LeaseExpiresAt is null || LeaseExpiresAt <= at)
            throw new InvalidOperationException("content_publish_attempt_lease_expired");
    }

    private void ClearLease()
    {
        LeaseToken = null;
        LeaseExpiresAt = null;
    }

    private static string BuildIdempotencyKey(
        Guid tenantId,
        Guid scheduleId,
        int contentRevision,
        Guid publishTargetId,
        int attemptSequence) =>
        attemptSequence == 1
            ? $"content-publish:{tenantId:N}:{scheduleId:N}:{contentRevision}:{publishTargetId:N}"
            : $"content-publish:{tenantId:N}:{scheduleId:N}:{contentRevision}:{publishTargetId:N}:{attemptSequence}";

    private static byte[] ComputeSnapshotHash(
        Guid tenantId,
        Guid scheduleId,
        Guid contentItemId,
        int contentRevision,
        string platform,
        Guid publishTargetId,
        string bodySnapshot,
        string assetsSnapshotJson)
    {
        var canonical = string.Join(
            '\n',
            CurrentSnapshotSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tenantId.ToString("N"),
            scheduleId.ToString("N"),
            contentItemId.ToString("N"),
            contentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            platform,
            publishTargetId.ToString("N"),
            bodySnapshot.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bodySnapshot,
            assetsSnapshotJson.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            assetsSnapshotJson);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private static string BuildAssetsSnapshot(
        IReadOnlyList<ContentPublishAssetSnapshot> assetSnapshots)
    {
        ArgumentNullException.ThrowIfNull(assetSnapshots);
        if (assetSnapshots.Count > MaxAssetCount)
            throw new ArgumentException("content_publish_attempt_asset_count_exceeded", nameof(assetSnapshots));

        var assetIds = new HashSet<Guid>();
        var sortOrders = new HashSet<int>();
        var normalized = new List<ContentPublishAssetSnapshot>(assetSnapshots.Count);
        foreach (var asset in assetSnapshots.OrderBy(value => value.SortOrder).ThenBy(value => value.AssetId))
        {
            ValidateIdentity(asset.AssetId, nameof(assetSnapshots));
            if (!assetIds.Add(asset.AssetId))
                throw new ArgumentException("content_publish_attempt_asset_duplicate", nameof(assetSnapshots));
            if (asset.SortOrder < 0 || !sortOrders.Add(asset.SortOrder))
                throw new ArgumentException("content_publish_attempt_asset_order_invalid", nameof(assetSnapshots));
            if (asset.SizeBytes <= 0)
                throw new ArgumentException("content_publish_attempt_asset_size_invalid", nameof(assetSnapshots));
            if (asset.Sha256Hex.Length != Sha256HexLength
                || asset.Sha256Hex.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new ArgumentException("content_publish_attempt_asset_hash_invalid", nameof(assetSnapshots));
            }

            normalized.Add(asset with
            {
                Sha256Hex = asset.Sha256Hex.ToLowerInvariant(),
                ContentType = NormalizeRequired(
                    asset.ContentType,
                    ContentAsset.MaxContentTypeLength,
                    "content_publish_attempt_asset_content_type_invalid").ToLowerInvariant(),
            });
        }

        return JsonSerializer.Serialize(normalized, SnapshotJsonOptions);
    }

    private static string NormalizeRequired(string value, int maxLength, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(errorCode, nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException(errorCode, nameof(value));
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException("content_publish_attempt_identifier_too_long", nameof(value));
        return normalized;
    }

    private static void ValidateIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("content_publish_attempt_identity_required", parameterName);
    }
}
