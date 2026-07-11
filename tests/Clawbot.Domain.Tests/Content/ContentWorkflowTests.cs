using Clawbot.Domain.Content;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentWorkflowTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 7, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ContentBrief_update_changes_editable_fields_and_updated_at()
    {
        var brief = ContentBrief.Create(TenantId, "facebook", "Old brief", createdBy: null, CreatedAt);
        var updatedAt = CreatedAt.AddHours(1);

        brief.Update("zalo", "New brief", updatedAt);

        brief.Platform.Should().Be("zalo");
        brief.Brief.Should().Be("New brief");
        brief.UpdatedAt.Should().Be(updatedAt);
        brief.Status.Should().Be("pending");
    }

    [Fact]
    public void ContentBrief_mark_status_changes_status_and_updated_at()
    {
        var brief = ContentBrief.Create(TenantId, "facebook", "Trend brief", createdBy: null, CreatedAt);
        var updatedAt = CreatedAt.AddHours(2);

        brief.MarkStatus("approved", updatedAt);

        brief.Status.Should().Be("approved");
        brief.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ContentItem_mark_published_throws_when_review_required_and_unsigned()
    {
        // Review-gate P1 domain backstop: no publish path can emit an unreviewed item when the tenant flag is on.
        var item = ContentItem.Create(TenantId, "facebook", "body", createdBy: null, CreatedAt);
        item.Approve(Guid.NewGuid(), CreatedAt.AddHours(1)); // human only — no agent signoff

        var act = () => item.MarkPublished(CreatedAt.AddHours(2), requireAgentReview: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*content_review_required*");
        item.Status.Should().Be("approved");
    }

    [Fact]
    public void ContentItem_mark_published_succeeds_with_agent_signoff_when_review_required()
    {
        var item = ContentItem.Create(TenantId, "facebook", "body", createdBy: null, CreatedAt);
        item.ApproveByAgent(Guid.NewGuid(), CreatedAt.AddHours(1));

        item.MarkPublished(CreatedAt.AddHours(2), requireAgentReview: true);

        item.Status.Should().Be("published");
    }

    [Fact]
    public void ContentItem_reject_persists_reason()
    {
        var item = ContentItem.Create(TenantId, "facebook", "body", createdBy: null, CreatedAt);

        item.Reject(CreatedAt.AddHours(1), "bịa giá khuyến mãi");

        item.Status.Should().Be("rejected");
        item.RejectedReason.Should().Be("bịa giá khuyến mãi");
    }

    [Fact]
    public void ContentItem_update_body_keeps_status_and_sets_updated_at()
    {
        var item = ContentItem.Create(TenantId, "tiktok", "Draft v1", createdBy: null, CreatedAt);
        var updatedAt = CreatedAt.AddMinutes(30);

        item.UpdateBody("Draft v2", updatedAt);

        item.Body.Should().Be("Draft v2");
        item.UpdatedAt.Should().Be(updatedAt);
        item.Status.Should().Be("draft");
    }

    [Fact]
    public void ContentItem_create_can_link_to_source_brief()
    {
        var briefId = Guid.NewGuid();

        var item = ContentItem.Create(TenantId, "zalo", "Draft", createdBy: null, CreatedAt, briefId);

        item.BriefId.Should().Be(briefId);
    }

    [Fact]
    public void ContentItem_mark_scheduled_and_published_advance_status_and_updated_at()
    {
        var item = ContentItem.Create(TenantId, "instagram", "Post body", createdBy: null, CreatedAt);
        var scheduledAt = CreatedAt.AddDays(1);
        var publishedAt = CreatedAt.AddDays(2);

        item.MarkScheduled(scheduledAt);
        item.Status.Should().Be("scheduled");
        item.UpdatedAt.Should().Be(scheduledAt);

        item.MarkPublished(publishedAt);
        item.Status.Should().Be("published");
        item.UpdatedAt.Should().Be(publishedAt);
    }

    [Fact]
    public void ContentItem_approve_records_audit_and_updated_at()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        var approver = Guid.NewGuid();
        var approvedAt = CreatedAt.AddHours(3);

        item.Approve(approver, approvedAt);

        item.Status.Should().Be("approved");
        item.ApprovedBy.Should().Be(approver);
        item.ApprovedAt.Should().Be(approvedAt);
        item.UpdatedAt.Should().Be(approvedAt);
    }

    [Fact]
    public void ContentItem_reject_sets_status_and_updated_at()
    {
        var item = ContentItem.Create(TenantId, "youtube", "Draft", createdBy: null, CreatedAt);
        var rejectedAt = CreatedAt.AddHours(4);

        item.Reject(rejectedAt);

        item.Status.Should().Be("rejected");
        item.UpdatedAt.Should().Be(rejectedAt);
    }

    [Fact]
    public void ContentItem_soft_delete_sets_deleted_at_and_updated_at()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Body", createdBy: null, CreatedAt);
        var deletedAt = CreatedAt.AddHours(5);

        item.SoftDelete(deletedAt);

        item.DeletedAt.Should().Be(deletedAt);
        item.UpdatedAt.Should().Be(deletedAt);
        item.Status.Should().Be("draft");
    }

    [Fact]
    public void ContentItem_set_assets_updates_json_and_updated_at()
    {
        var item = ContentItem.Create(TenantId, "tiktok", "Body", createdBy: null, CreatedAt);
        var updatedAt = CreatedAt.AddMinutes(15);

        item.SetAssets("[{\"url\":\"https://cdn.example/img.jpg\"}]", updatedAt);

        item.AssetsJson.Should().Be("[{\"url\":\"https://cdn.example/img.jpg\"}]");
        item.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ContentItem_revert_to_approved_resets_status_and_updated_at()
    {
        var item = ContentItem.Create(TenantId, "instagram", "Body", createdBy: null, CreatedAt);
        item.MarkScheduled(CreatedAt.AddHours(1));
        var revertedAt = CreatedAt.AddHours(2);

        item.RevertToApproved(revertedAt);

        item.Status.Should().Be("approved");
        item.UpdatedAt.Should().Be(revertedAt);
    }

    [Fact]
    public void ContentSchedule_record_retry_increments_count_and_stays_pending()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), "facebook", CreatedAt.AddHours(3), CreatedAt);
        var at = CreatedAt.AddHours(4);

        var willRetry = schedule.RecordRetry(at);

        willRetry.Should().BeTrue();
        schedule.RetryCount.Should().Be(1);
        schedule.Status.Should().Be("pending");
        schedule.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void ContentSchedule_record_retry_returns_false_at_max_retries()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), "youtube", CreatedAt.AddHours(3), CreatedAt);
        var at = CreatedAt.AddHours(4);

        for (var i = 0; i < ContentSchedule.MaxRetries - 1; i++)
            schedule.RecordRetry(at);

        var finalRetry = schedule.RecordRetry(at);

        finalRetry.Should().BeFalse();
        schedule.RetryCount.Should().Be(ContentSchedule.MaxRetries);
        schedule.Status.Should().Be("failed");
    }

    [Fact]
    public void ContentSchedule_mark_posted_failed_and_canceled_update_status_and_audit_time()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId,
            Guid.NewGuid(),
            "facebook",
            CreatedAt.AddHours(3),
            CreatedAt);
        var postedAt = CreatedAt.AddHours(4);

        schedule.MarkPosted("https://social.example/posts/1", postedAt);

        schedule.Status.Should().Be("posted");
        schedule.PostedAt.Should().Be(postedAt);
        schedule.PostUrl.Should().Be("https://social.example/posts/1");
        schedule.UpdatedAt.Should().Be(postedAt);

        var failedAt = postedAt.AddHours(1);
        schedule.MarkFailed(failedAt);
        schedule.Status.Should().Be("failed");
        schedule.UpdatedAt.Should().Be(failedAt);

        var canceledAt = failedAt.AddHours(1);
        schedule.Cancel(canceledAt);
        schedule.Status.Should().Be("canceled");
        schedule.UpdatedAt.Should().Be(canceledAt);
    }
}
