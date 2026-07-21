using Clawbot.Api.Endpoints;
using Clawbot.Domain.Content;
using FluentAssertions;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class ContentCalendarTests
{
    [Fact]
    public void BuildCalendarRows_joins_schedule_with_item_body()
    {
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);
        var item = ContentItem.Create(tenantId, "instagram", "Carousel body", createdBy: null, now);
        var schedule = ContentSchedule.Schedule(tenantId, item.Id, item.ContentRevision, "instagram", now.AddHours(3), now);

        var rows = ContentEndpoints.BuildCalendarRows([schedule], new Dictionary<Guid, ContentItem>
        {
            [item.Id] = item,
        });

        rows.Should().ContainSingle();
        rows[0].ContentItemId.Should().Be(item.Id);
        rows[0].ScheduleId.Should().Be(schedule.Id);
        rows[0].Body.Should().Be("Carousel body");
        rows[0].ScheduledAt.Should().Be(schedule.ScheduledAt);
    }

    [Fact]
    public void BuildCalendarRows_excludes_schedule_when_item_missing()
    {
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);
        var schedule = ContentSchedule.Schedule(tenantId, Guid.NewGuid(), 1, "facebook", now.AddHours(3), now);

        var rows = ContentEndpoints.BuildCalendarRows([schedule], new Dictionary<Guid, ContentItem>());

        rows.Should().BeEmpty();
    }

    [Fact]
    public void AddImageAsset_appends_uploaded_image_metadata()
    {
        var assetsJson = ContentEndpoints.AddImageAsset(
            "[]",
            "https://cdn.example/content.png",
            "content.png",
            "image/png");

        assetsJson.Should().Contain("https://cdn.example/content.png");
        assetsJson.Should().Contain("content.png");
        assetsJson.Should().Contain("image/png");
    }

    [Fact]
    public void TryNormalizeAssetsJson_rejects_non_image_payload()
    {
        var ok = ContentEndpoints.TryNormalizeAssetsJson("""[{"type":"html","url":"javascript:alert(1)"}]""", out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeAllowedImage_matches_declared_png_signature()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        ContentEndpoints.LooksLikeAllowedImage(bytes, "image/png").Should().BeTrue();
        ContentEndpoints.LooksLikeAllowedImage(bytes, "image/jpeg").Should().BeFalse();
    }

    [Fact]
    public void BuildCalendarRows_maps_all_status_variants()
    {
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);
        var item1 = ContentItem.Create(tenantId, "tiktok", "Post 1", createdBy: null, now);
        var item2 = ContentItem.Create(tenantId, "youtube", "Post 2", createdBy: null, now);
        var s1 = ContentSchedule.Schedule(tenantId, item1.Id, item1.ContentRevision, "tiktok", now.AddHours(3), now);
        var s2 = ContentSchedule.Schedule(tenantId, item2.Id, item2.ContentRevision, "youtube", now.AddHours(5), now);
        s2.MarkPublishing(now.AddHours(5));
        s2.MarkPosted("https://social.example/p/2", now.AddHours(6));

        var rows = ContentEndpoints.BuildCalendarRows(
            [s1, s2],
            new Dictionary<Guid, ContentItem> { [item1.Id] = item1, [item2.Id] = item2 });

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.Status == "pending" && r.ContentItemId == item1.Id);
        rows.Should().Contain(r => r.Status == "posted" && r.PostUrl == "https://social.example/p/2");
    }
}
