using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawbot.Api.Services;
using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Api.Tests.Services;

public sealed class ContentAssetLifecycleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 3, 0, 0, TimeSpan.Zero);

    private static ContentAsset UploadingAsset(
        int sortOrder,
        string fileName = "a.png",
        DateTimeOffset? createdAt = null) =>
        ContentAsset.Reserve(
            TenantId,
            Guid.NewGuid(),
            fileName,
            ".png",
            sortOrder,
            createdAt ?? Now);

    private static ContentAsset ReadyAsset(
        int sortOrder,
        string fileName = "a.png",
        DateTimeOffset? createdAt = null)
    {
        var asset = UploadingAsset(sortOrder, fileName, createdAt);
        asset.MarkReady(new byte[32], 1024, "image/png", createdAt ?? Now);
        return asset;
    }

    [Fact]
    public void ComputeSha256_MatchesFrameworkHash()
    {
        var bytes = Encoding.UTF8.GetBytes("clawbot");

        ContentAssetLifecycle.ComputeSha256(bytes).Should().Equal(SHA256.HashData(bytes));
    }

    [Fact]
    public void ComputeSha256_EmptyInput_ReturnsEmptyHash()
    {
        ContentAssetLifecycle.ComputeSha256([]).Should().Equal(SHA256.HashData([]));
    }

    [Fact]
    public void ComputeSha256_AlwaysReturns32Bytes()
    {
        ContentAssetLifecycle.ComputeSha256(new byte[1000]).Should().HaveCount(32);
    }

    [Fact]
    public void ManualEditQuietPeriod_Is30Seconds()
    {
        ContentAssetLifecycle.ManualEditQuietPeriod.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CreateQuietPeriodReviewTask_SchedulesAfterQuietPeriod()
    {
        var itemId = Guid.NewGuid();

        var task = ContentAssetLifecycle.CreateQuietPeriodReviewTask(TenantId, itemId, 3, Now);

        task.TenantId.Should().Be(TenantId);
        task.ContentItemId.Should().Be(itemId);
        task.ContentRevision.Should().Be(3);
        task.NextAttemptAt.Should().Be(Now.AddSeconds(30));
    }

    [Fact]
    public void NextSortOrder_NoAssets_StartsAtZero()
    {
        ContentAssetLifecycle.NextSortOrder([]).Should().Be(0);
    }

    [Fact]
    public void NextSortOrder_ContinuesAfterHighestReadySlot()
    {
        ContentAssetLifecycle.NextSortOrder([ReadyAsset(0), ReadyAsset(4), ReadyAsset(2)])
            .Should().Be(5);
    }

    [Fact]
    public void NextSortOrder_CountsUploadingAssets()
    {
        // Ảnh đang upload đã chiếm slot — không được cấp lại số thứ tự đó.
        ContentAssetLifecycle.NextSortOrder([UploadingAsset(7, "b.png")]).Should().Be(8);
    }

    [Fact]
    public void BuildDerivedAssetsJson_NoAssets_ReturnsEmptyArray()
    {
        ContentAssetLifecycle.BuildDerivedAssetsJson([], new Dictionary<Guid, string>())
            .Should().Be("[]");
    }

    [Fact]
    public void BuildDerivedAssetsJson_EmitsDisplayFields()
    {
        var asset = ReadyAsset(0, "poster.png");
        var urls = new Dictionary<Guid, string> { [asset.Id] = "/api/content/assets/1" };

        var json = ContentAssetLifecycle.BuildDerivedAssetsJson([asset], urls);

        var element = JsonSerializer.Deserialize<JsonElement>(json)[0];
        element.GetProperty("type").GetString().Should().Be("image");
        element.GetProperty("url").GetString().Should().Be("/api/content/assets/1");
        element.GetProperty("fileName").GetString().Should().Be("poster.png");
        element.GetProperty("contentType").GetString().Should().Be("image/png");
        element.GetProperty("assetId").GetString().Should().Be(asset.Id.ToString("D"));
    }

    [Fact]
    public void BuildDerivedAssetsJson_OrdersBySortOrderThenCreatedAt()
    {
        var second = ReadyAsset(1, "second.png");
        var first = ReadyAsset(0, "first.png");
        var urls = new Dictionary<Guid, string>
        {
            [first.Id] = "/first",
            [second.Id] = "/second",
        };

        var json = ContentAssetLifecycle.BuildDerivedAssetsJson([second, first], urls);

        var array = JsonSerializer.Deserialize<JsonElement>(json);
        array[0].GetProperty("fileName").GetString().Should().Be("first.png");
        array[1].GetProperty("fileName").GetString().Should().Be("second.png");
    }

    [Fact]
    public void BuildDerivedAssetsJson_SkipsAssetWithoutDisplayUrl()
    {
        // Ảnh chưa ký được URL thì bỏ khỏi JSON hiển thị, không render link rỗng ra FE.
        var withUrl = ReadyAsset(0, "ok.png");
        var withoutUrl = ReadyAsset(1, "missing.png");
        var urls = new Dictionary<Guid, string> { [withUrl.Id] = "/ok" };

        var json = ContentAssetLifecycle.BuildDerivedAssetsJson([withUrl, withoutUrl], urls);

        JsonSerializer.Deserialize<JsonElement>(json).GetArrayLength().Should().Be(1);
        json.Should().NotContain("missing.png");
    }

    [Fact]
    public void BuildDerivedAssetsJson_SkipsBlankDisplayUrl()
    {
        var asset = ReadyAsset(0);
        var urls = new Dictionary<Guid, string> { [asset.Id] = "   " };

        ContentAssetLifecycle.BuildDerivedAssetsJson([asset], urls).Should().Be("[]");
    }

    [Fact]
    public void BuildDerivedAssetsJson_SkipsNonReadyAssets()
    {
        var uploading = UploadingAsset(0, "pending.png");
        var urls = new Dictionary<Guid, string> { [uploading.Id] = "/pending" };

        ContentAssetLifecycle.BuildDerivedAssetsJson([uploading], urls).Should().Be("[]");
    }
}
