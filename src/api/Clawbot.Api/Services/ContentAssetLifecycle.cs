using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clawbot.Domain.Content;

namespace Clawbot.Api.Services;

// Phase 2.9: server-owned content_assets helpers. AssetsJson is derived display JSON only.
public static class ContentAssetLifecycle
{
    public static readonly TimeSpan ManualEditQuietPeriod = TimeSpan.FromSeconds(30);

    public static byte[] ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        var hash = new byte[32];
        if (!SHA256.TryHashData(bytes, hash, out var written) || written != 32)
            throw new InvalidOperationException("content_asset_sha256_failed");
        return hash;
    }

    public static string BuildDerivedAssetsJson(
        IEnumerable<ContentAsset> readyAssets,
        IReadOnlyDictionary<Guid, string> displayUrlsByAssetId)
    {
        var assets = new JsonArray();
        foreach (var asset in readyAssets
                     .Where(a => a.Status == ContentAsset.StatusReady)
                     .OrderBy(a => a.SortOrder)
                     .ThenBy(a => a.CreatedAt))
        {
            if (!displayUrlsByAssetId.TryGetValue(asset.Id, out var url) || string.IsNullOrWhiteSpace(url))
                continue;

            assets.Add(new JsonObject
            {
                ["type"] = "image",
                ["url"] = url,
                ["fileName"] = asset.OriginalFileName,
                ["contentType"] = asset.ContentType,
                ["assetId"] = asset.Id.ToString("D"),
            });
        }

        return assets.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static ContentReviewTask CreateQuietPeriodReviewTask(
        Guid tenantId,
        Guid contentItemId,
        int contentRevision,
        DateTimeOffset at) =>
        ContentReviewTask.CreatePending(
            tenantId,
            contentItemId,
            contentRevision,
            nextAttemptAt: at.Add(ManualEditQuietPeriod),
            createdAt: at);

    public static int NextSortOrder(IEnumerable<ContentAsset> existing) =>
        existing
            .Where(a => a.Status is ContentAsset.StatusReady or ContentAsset.StatusUploading)
            .Select(a => a.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;
}
