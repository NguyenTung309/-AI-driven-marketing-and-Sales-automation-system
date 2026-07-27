using System.Security.Cryptography;
using System.Text;
using Clawbot.Agents.Core.Docs;
using Clawbot.Domain.Content;

namespace Clawbot.Agents.Core.Content;

// Phase 2.10: tenant/item/asset scoped bounded reads. Never trusts client storage keys or AssetsJson.

public sealed record ContentAssetStat(
    Guid AssetId,
    Guid TenantId,
    Guid ContentItemId,
    string StorageKey,
    string ContentType,
    long SizeBytes,
    IReadOnlyList<byte> Sha256,
    int SortOrder);

public sealed record ContentAssetBytes(
    ContentAssetStat Stat,
    IReadOnlyList<byte> Bytes);

public interface IContentAssetReader
{
    Task<IReadOnlyList<ContentAssetStat>> ListReadyAsync(
        Guid tenantId,
        Guid contentItemId,
        CancellationToken cancellationToken);

    Task<ContentAssetStat> StatAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        CancellationToken cancellationToken);

    Task<ContentAssetBytes> ReadAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        CancellationToken cancellationToken);
}

public sealed class ContentAssetReaderOptions
{
    public const string SectionName = "Content:Assets";
    public long MaxBytesPerAsset { get; set; } = 5 * 1024 * 1024;
    public int MaxAssetsPerItem { get; set; } = 10;
}

public interface IContentAssetRepository
{
    Task<IReadOnlyList<ContentAsset>> ListReadyAsync(
        Guid tenantId,
        Guid contentItemId,
        CancellationToken cancellationToken);

    Task<ContentAsset?> FindReadyAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        CancellationToken cancellationToken);
}

public sealed class ContentAssetReader(
    IContentAssetRepository repository,
    IDocumentStorage storage,
    ContentAssetReaderOptions? options = null) : IContentAssetReader
{
    private readonly IContentAssetRepository _repository = repository
        ?? throw new ArgumentNullException(nameof(repository));
    private readonly IDocumentStorage _storage = storage
        ?? throw new ArgumentNullException(nameof(storage));
    private readonly ContentAssetReaderOptions _options = options ?? new ContentAssetReaderOptions();

    public async Task<IReadOnlyList<ContentAssetStat>> ListReadyAsync(
        Guid tenantId,
        Guid contentItemId,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(contentItemId, nameof(contentItemId));

        var rows = await _repository.ListReadyAsync(tenantId, contentItemId, cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count > _options.MaxAssetsPerItem)
            throw new InvalidOperationException("content_asset_count_exceeded");

        return rows
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .Select(ToStat)
            .ToArray();
    }

    public async Task<ContentAssetStat> StatAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var asset = await LoadReadyAsync(tenantId, contentItemId, assetId, cancellationToken)
            .ConfigureAwait(false);
        return ToStat(asset);
    }

    public async Task<ContentAssetBytes> ReadAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var asset = await LoadReadyAsync(tenantId, contentItemId, assetId, cancellationToken)
            .ConfigureAwait(false);
        var stat = ToStat(asset);

        ValidateStorageKey(tenantId, contentItemId, assetId, asset.StorageKey);

        byte[] bytes;
        try
        {
            bytes = await _storage.ReadAsync(asset.StorageKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("content_asset_read_failed", ex);
        }

        if (bytes.Length == 0)
            throw new InvalidOperationException("content_asset_empty");
        if (bytes.LongLength > _options.MaxBytesPerAsset)
            throw new InvalidOperationException("content_asset_too_large");
        if (asset.SizeBytes is long expectedSize && expectedSize != bytes.LongLength)
            throw new InvalidOperationException("content_asset_size_mismatch");

        var digest = SHA256.HashData(bytes);
        var expected = asset.Sha256 ?? throw new InvalidOperationException("content_asset_sha256_missing");
        if (!CryptographicOperations.FixedTimeEquals(digest, expected))
            throw new InvalidOperationException("content_asset_integrity_mismatch");

        if (!LooksLikeDeclaredImage(bytes, asset.ContentType))
            throw new InvalidOperationException("content_asset_content_type_mismatch");

        return new ContentAssetBytes(stat, bytes);
    }

    private async Task<ContentAsset> LoadReadyAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(contentItemId, nameof(contentItemId));
        ValidateIdentity(assetId, nameof(assetId));

        var asset = await _repository.FindReadyAsync(tenantId, contentItemId, assetId, cancellationToken)
            .ConfigureAwait(false);
        if (asset is null)
            throw new InvalidOperationException("content_asset_not_found");
        if (asset.TenantId != tenantId || asset.ContentItemId != contentItemId || asset.Id != assetId)
            throw new InvalidOperationException("content_asset_tenant_mismatch");
        if (asset.Status != ContentAsset.StatusReady)
            throw new InvalidOperationException("content_asset_not_ready");

        ValidateStorageKey(tenantId, contentItemId, assetId, asset.StorageKey);
        return asset;
    }

    internal static void ValidateStorageKey(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new InvalidOperationException("content_asset_storage_key_invalid");

        // Reject absolute paths, backslashes, dot segments, query/fragment, encoded separators.
        if (storageKey.Contains('\\')
            || storageKey.Contains('\0')
            || storageKey.Contains('?')
            || storageKey.Contains('#')
            || storageKey.Contains('%')
            || storageKey.Contains(':')
            || storageKey.StartsWith('/')
            || storageKey.Contains("//", StringComparison.Ordinal)
            || storageKey.Split('/', StringSplitOptions.None).Any(IsDotSegment))
        {
            throw new InvalidOperationException("content_asset_storage_key_invalid");
        }

        var expected = $"tenants/{tenantId:N}/content/{contentItemId:N}/{assetId:N}";
        if (!string.Equals(storageKey, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("content_asset_storage_key_mismatch");
    }

    private static bool IsDotSegment(string segment) =>
        segment.Length == 0
        || segment == "."
        || segment == ".."
        || segment.Equals("%2e", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase);

    private static ContentAssetStat ToStat(ContentAsset asset)
    {
        if (asset.SizeBytes is null || asset.SizeBytes <= 0)
            throw new InvalidOperationException("content_asset_size_missing");
        if (string.IsNullOrWhiteSpace(asset.ContentType))
            throw new InvalidOperationException("content_asset_content_type_missing");
        var sha = asset.Sha256 ?? throw new InvalidOperationException("content_asset_sha256_missing");
        return new ContentAssetStat(
            asset.Id,
            asset.TenantId,
            asset.ContentItemId,
            asset.StorageKey,
            asset.ContentType!,
            asset.SizeBytes.Value,
            sha,
            asset.SortOrder);
    }

    private static void ValidateIdentity(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("content_asset_identity_required", name);
    }

    private static bool LooksLikeDeclaredImage(ReadOnlySpan<byte> bytes, string? contentType)
    {
        if (bytes.Length < 12)
            return false;
        var type = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8;
        var isGif = bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F';
        var isWebp = bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';

        return type switch
        {
            "image/png" => isPng,
            "image/jpeg" or "image/jpg" => isJpeg,
            "image/gif" => isGif,
            "image/webp" => isWebp,
            _ => isPng || isJpeg || isGif || isWebp,
        };
    }
}
