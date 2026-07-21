using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentAsset : Entity<Guid>, ITenantOwned, IAuditExempt
{
    public const string StatusUploading = "uploading";
    public const string StatusReady = "ready";
    public const string StatusDeletePending = "delete_pending";
    public const string StatusFailed = "failed";
    public const string StatusDeleted = "deleted";

    private const int MaxFileNameLength = 255;
    public const int MaxContentTypeLength = 128;
    private const int MaxErrorCodeLength = 128;

    private byte[]? _sha256;

    public Guid TenantId { get; private set; }
    public Guid ContentItemId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = StatusUploading;
    public byte[]? Sha256 => _sha256?.ToArray();
    public long? SizeBytes { get; private set; }
    public string? ContentType { get; private set; }
    public string? OriginalFileName { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ContentAsset() { }

    public static ContentAsset Reserve(
        Guid tenantId,
        Guid contentItemId,
        string? originalFileName,
        int sortOrder,
        DateTimeOffset createdAt)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(contentItemId, nameof(contentItemId));
        ArgumentOutOfRangeException.ThrowIfNegative(sortOrder);

        var id = Guid.NewGuid();
        return new ContentAsset
        {
            Id = id,
            TenantId = tenantId,
            ContentItemId = contentItemId,
            StorageKey = $"tenants/{tenantId:N}/content/{contentItemId:N}/{id:N}",
            OriginalFileName = NormalizeFileName(originalFileName),
            SortOrder = sortOrder,
            CreatedAt = createdAt,
        };
    }

    public void MarkReady(
        byte[] sha256,
        long sizeBytes,
        string contentType,
        DateTimeOffset at)
    {
        if (Status != StatusUploading)
            throw new InvalidOperationException("content_asset_not_uploading");
        ArgumentNullException.ThrowIfNull(sha256);
        if (sha256.Length != 32)
            throw new ArgumentException("content_asset_sha256_invalid", nameof(sha256));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        var normalizedContentType = NormalizeRequired(
            contentType,
            MaxContentTypeLength,
            "content_asset_content_type_required");

        _sha256 = sha256.ToArray();
        SizeBytes = sizeBytes;
        ContentType = normalizedContentType;
        Status = StatusReady;
        ReadyAt = at;
        LastErrorCode = null;
    }

    public void MarkFailed(string errorCode, DateTimeOffset at)
    {
        _ = at;
        if (Status != StatusUploading)
            throw new InvalidOperationException("content_asset_not_uploading");

        Status = StatusFailed;
        LastErrorCode = NormalizeErrorCode(errorCode);
    }

    public void MarkDeletePending(string? errorCode, DateTimeOffset at)
    {
        _ = at;
        if (Status is StatusDeletePending or StatusDeleted)
            throw new InvalidOperationException("content_asset_delete_not_allowed");

        Status = StatusDeletePending;
        LastErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : NormalizeErrorCode(errorCode);
    }

    public void MarkDeleted(DateTimeOffset at)
    {
        if (Status != StatusDeletePending)
            throw new InvalidOperationException("content_asset_delete_not_pending");

        Status = StatusDeleted;
        DeletedAt = at;
        LastErrorCode = null;
    }

    private static string? NormalizeFileName(string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            return null;

        var normalizedSeparators = originalFileName.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedSeparators).Trim();
        if (fileName.Length == 0
            || fileName.Length > MaxFileNameLength
            || fileName.Any(char.IsControl))
        {
            throw new ArgumentException("content_asset_file_name_invalid", nameof(originalFileName));
        }

        return fileName;
    }

    private static string NormalizeErrorCode(string errorCode) =>
        NormalizeRequired(errorCode, MaxErrorCodeLength, "content_asset_error_code_required");

    private static string NormalizeRequired(string value, int maxLength, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(errorCode, nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentException(errorCode, nameof(value));
        return normalized;
    }

    private static void ValidateIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("content_asset_identity_required", parameterName);
    }
}
