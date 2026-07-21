using Clawbot.Api.Services;
using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ContentAssetLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeSha256_is_32_bytes_and_deterministic()
    {
        var bytes = "png-bytes"u8.ToArray();
        var a = ContentAssetLifecycle.ComputeSha256(bytes);
        var b = ContentAssetLifecycle.ComputeSha256(bytes);

        a.Should().HaveCount(32);
        a.Should().Equal(b);
        a.Should().NotEqual(ContentAssetLifecycle.ComputeSha256("other"u8.ToArray()));
    }

    [Fact]
    public void BuildDerivedAssetsJson_orders_ready_assets_and_embeds_asset_id()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var second = ContentAsset.Reserve(tenantId, itemId, "b.png", sortOrder: 1, createdAt: Now);
        second.MarkReady(new byte[32], 10, "image/png", Now);
        var first = ContentAsset.Reserve(tenantId, itemId, "a.png", sortOrder: 0, createdAt: Now.AddSeconds(-1));
        first.MarkReady(new byte[32], 20, "image/jpeg", Now);
        var uploading = ContentAsset.Reserve(tenantId, itemId, "c.png", sortOrder: 2, createdAt: Now);

        var json = ContentAssetLifecycle.BuildDerivedAssetsJson(
            [second, first, uploading],
            new Dictionary<Guid, string>
            {
                [first.Id] = "https://cdn.example/a.jpg",
                [second.Id] = "https://cdn.example/b.png",
            });

        json.Should().Contain(first.Id.ToString("D"));
        json.Should().Contain(second.Id.ToString("D"));
        json.Should().NotContain(uploading.Id.ToString("D"));
        json.IndexOf("a.jpg", StringComparison.Ordinal).Should().BeLessThan(
            json.IndexOf("b.png", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateQuietPeriodReviewTask_is_pending_for_revision_with_delay()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var task = ContentAssetLifecycle.CreateQuietPeriodReviewTask(tenantId, itemId, contentRevision: 3, Now);

        task.TenantId.Should().Be(tenantId);
        task.ContentItemId.Should().Be(itemId);
        task.ContentRevision.Should().Be(3);
        task.Status.Should().Be(ContentReviewTask.StatusPending);
        task.NextAttemptAt.Should().Be(Now.Add(ContentAssetLifecycle.ManualEditQuietPeriod));
        task.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void NextSortOrder_skips_deleted_and_continues_from_active_max()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var ready = ContentAsset.Reserve(tenantId, itemId, "a.png", 0, Now);
        ready.MarkReady(new byte[32], 1, "image/png", Now);
        var uploading = ContentAsset.Reserve(tenantId, itemId, "b.png", 2, Now);
        var deleted = ContentAsset.Reserve(tenantId, itemId, "c.png", 9, Now);
        deleted.MarkDeletePending("gone", Now);
        deleted.MarkDeleted(Now);

        ContentAssetLifecycle.NextSortOrder([ready, uploading, deleted]).Should().Be(3);
        ContentAssetLifecycle.NextSortOrder([]).Should().Be(0);
    }
}
