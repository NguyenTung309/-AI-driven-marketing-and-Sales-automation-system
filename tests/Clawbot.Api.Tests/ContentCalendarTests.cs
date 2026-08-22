using Clawbot.Api.Endpoints;
using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ContentCalendarTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildCalendarRows_ExcludesTerminalSchedulesAndPublishedContent()
    {
        // Arrange
        var activeItem = CreateScheduledItem("Bài còn cần xử lý");
        var publishedItem = CreateScheduledItem("Bài đã đăng nhưng còn lịch cũ");
        publishedItem.MarkPublished(Now);
        var postedItem = CreateScheduledItem("Bài có lịch đã đăng");
        postedItem.MarkPublished(Now);
        var canceledItem = CreateScheduledItem("Bài có lịch đã hủy");

        var active = Schedule(activeItem);
        var stalePending = Schedule(publishedItem);
        var posted = Schedule(postedItem);
        posted.MarkPublishing(Now);
        posted.MarkPosted("https://www.facebook.com/example/posts/1", "post-1", Now);
        var canceled = Schedule(canceledItem);
        canceled.Cancel(Now, "canceled_by_user");

        var items = new Dictionary<Guid, ContentItem>
        {
            [activeItem.Id] = activeItem,
            [publishedItem.Id] = publishedItem,
            [postedItem.Id] = postedItem,
            [canceledItem.Id] = canceledItem,
        };

        // Act
        var rows = ContentEndpoints.BuildCalendarRows([active, stalePending, posted, canceled], items);

        // Assert
        rows.Should().ContainSingle();
        rows[0].ScheduleId.Should().Be(active.Id);
    }

    private static ContentItem CreateScheduledItem(string body)
    {
        var item = ContentItem.Create(TenantId, "facebook", body, Guid.NewGuid(), Now.AddHours(-2));
        item.BeginAgentReview(item.ContentRevision, Now.AddHours(-1));
        item.RecordAgentReview(
            item.ContentRevision,
            reviewStatus: ContentItem.ReviewStatusPassed,
            imageStatus: ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: null,
            at: Now.AddMinutes(-45));
        item.ApproveForPublishing(
            item.ContentRevision,
            userId: Guid.NewGuid(),
            appliedPolicy: ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            overrideReason: null,
            at: Now.AddMinutes(-30));
        item.MarkScheduled(Now.AddMinutes(-15));
        return item;
    }

    private static ContentSchedule Schedule(ContentItem item) =>
        ContentSchedule.Schedule(
            TenantId,
            item.Id,
            item.ContentRevision,
            "facebook",
            Now.AddHours(2),
            Now);
}
