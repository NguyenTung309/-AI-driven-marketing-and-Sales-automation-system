using System.Reflection;
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
    public void ContentItem_legacy_human_approval_does_not_bypass_current_revision_backstop()
    {
        var item = ContentItem.Create(TenantId, "facebook", "body", createdBy: null, CreatedAt);
        item.Approve(Guid.NewGuid(), CreatedAt.AddHours(1));

        var act = () => item.MarkPublished(CreatedAt.AddHours(2), requireAgentReview: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_current_revision_not_publishable*");
        item.Status.Should().Be("approved");
        item.ApprovedRevision.Should().BeNull();
    }

    [Fact]
    public void ContentItem_legacy_agent_signoff_does_not_bypass_revision_review_and_approval()
    {
        var item = ContentItem.Create(TenantId, "facebook", "body", createdBy: null, CreatedAt);
        item.ApproveByAgent(Guid.NewGuid(), CreatedAt.AddHours(1));

        var act = () => item.MarkScheduled(CreatedAt.AddHours(2));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_current_revision_not_schedulable*");
        item.AgentReviewedRevision.Should().BeNull();
        item.ApprovedRevision.Should().BeNull();
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
        CompletePassedReviewAndAutomaticApproval(item);

        item.MarkScheduled(scheduledAt);
        item.Status.Should().Be("scheduled");
        item.UpdatedAt.Should().Be(scheduledAt);

        item.MarkPublished(publishedAt);
        item.Status.Should().Be("published");
        item.UpdatedAt.Should().Be(publishedAt);
    }

    [Fact]
    public void ContentItem_attach_legacy_agent_signoff_does_not_create_revision_review()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Post body", createdBy: null, CreatedAt);
        var agentId = Guid.NewGuid();

        item.AttachAgentSignoff(agentId, CreatedAt.AddHours(1));

        item.ApprovedByAgentId.Should().Be(agentId);
        item.AgentReviewedRevision.Should().BeNull();
        item.ApprovedRevision.Should().BeNull();
        item.Invoking(i => i.MarkScheduled(CreatedAt.AddHours(2)))
            .Should().Throw<InvalidOperationException>();
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
        CompletePassedReviewAndAutomaticApproval(item);
        item.MarkScheduled(CreatedAt.AddHours(1));
        var revertedAt = CreatedAt.AddHours(2);

        item.RevertToApproved(revertedAt);

        item.Status.Should().Be("approved");
        item.UpdatedAt.Should().Be(revertedAt);
    }

    [Theory]
    [InlineData("Approve")]
    [InlineData("ApproveByAgent")]
    [InlineData("Reject")]
    [InlineData("RevertToApproved")]
    [InlineData("AttachAgentSignoff")]
    public void ContentItem_legacy_status_bridges_reject_published_and_deleted_items(
        string mutator)
    {
        var published = ContentItem.Create(TenantId, "facebook", "Published", createdBy: null, CreatedAt);
        CompletePassedReviewAndAutomaticApproval(published);
        published.MarkScheduled(CreatedAt.AddMinutes(4));
        published.MarkPublished(CreatedAt.AddMinutes(5));

        InvokeLegacyBridge(published, mutator)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_published_item_immutable");
        published.Status.Should().Be("published");

        var deleted = ContentItem.Create(TenantId, "facebook", "Deleted", createdBy: null, CreatedAt);
        deleted.SoftDelete(CreatedAt.AddMinutes(1));

        InvokeLegacyBridge(deleted, mutator)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_item_deleted");
        deleted.Status.Should().Be("draft");
        deleted.DeletedAt.Should().Be(CreatedAt.AddMinutes(1));
    }

    [Fact]
    public void ContentItem_require_human_approval_accepts_only_closed_reason_codes()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);

        item.RequireHumanApproval(
            ContentItem.HumanApprovalReasonMigrationCutover,
            CreatedAt.AddMinutes(1));

        item.HumanApprovalRequirementReason
            .Should().Be(ContentItem.HumanApprovalReasonMigrationCutover);
        item.Status.Should().Be("draft");

        item.Invoking(value => value.RequireHumanApproval(
                "free-form operator note that exceeds closed codes",
                CreatedAt.AddMinutes(2)))
            .Should().Throw<ArgumentException>()
            .WithMessage("*content_human_approval_reason_invalid*");
    }

    [Fact]
    public void ContentSchedule_active_revision_slot_tracks_publishable_statuses()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), 3, "facebook", CreatedAt.AddHours(3), CreatedAt);

        schedule.ActiveRevisionSlot.Should().Be(3);

        schedule.MarkPublishing(CreatedAt.AddHours(4));
        schedule.ActiveRevisionSlot.Should().Be(3);

        schedule.MarkPosted("https://social.example/posts/1", CreatedAt.AddHours(5));
        schedule.ActiveRevisionSlot.Should().BeNull();
    }

    [Fact]
    public void ContentSchedule_failed_retry_restores_active_revision_slot()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), 2, "facebook", CreatedAt.AddHours(3), CreatedAt);

        schedule.MarkFailed(CreatedAt.AddHours(4), "publisher_down");
        schedule.ActiveRevisionSlot.Should().BeNull();

        schedule.RecordRetry(CreatedAt.AddHours(5), "publisher_down");
        schedule.ActiveRevisionSlot.Should().Be(2);
    }

    [Fact]
    public void ContentSchedule_provider_error_payload_is_reduced_to_safe_code()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), 1, "facebook", CreatedAt.AddHours(3), CreatedAt);

        schedule.MarkFailed(
            CreatedAt.AddHours(4),
            "{\"access_token\":\"provider-secret\"}");

        schedule.LastErrorCode.Should().Be(ContentSchedule.ErrorPublisherFailure);
        schedule.LastError.Should().Be(ContentSchedule.ErrorPublisherFailure);
    }

    [Fact]
    public void ContentSchedule_record_retry_increments_count_and_stays_pending()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), 1, "facebook", CreatedAt.AddHours(3), CreatedAt);
        var at = CreatedAt.AddHours(4);

        var willRetry = schedule.RecordRetry(at, "publisher_down");

        willRetry.Should().BeTrue();
        schedule.RetryCount.Should().Be(1);
        schedule.Status.Should().Be("pending");
        schedule.LastError.Should().Be("publisher_down");
        schedule.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void ContentSchedule_record_retry_returns_false_at_max_retries()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), 1, "youtube", CreatedAt.AddHours(3), CreatedAt);
        var at = CreatedAt.AddHours(4);

        for (var i = 0; i < ContentSchedule.MaxRetries - 1; i++)
            schedule.RecordRetry(at, "publisher_down");

        var finalRetry = schedule.RecordRetry(at, "publisher_down");

        finalRetry.Should().BeFalse();
        schedule.RetryCount.Should().Be(ContentSchedule.MaxRetries);
        schedule.Status.Should().Be("failed");
        schedule.LastError.Should().Be("publisher_down");
    }

    [Fact]
    public void ContentSchedule_publishing_to_posted_records_result_and_becomes_terminal()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId,
            Guid.NewGuid(),
            1,
            "facebook",
            CreatedAt.AddHours(3),
            CreatedAt);
        var publishingAt = CreatedAt.AddHours(4);
        var postedAt = publishingAt.AddMinutes(1);

        schedule.MarkPublishing(publishingAt);
        schedule.MarkPosted("https://social.example/posts/1", postedAt);

        schedule.Status.Should().Be(ContentSchedule.StatusPosted);
        schedule.PostedAt.Should().Be(postedAt);
        schedule.PostUrl.Should().Be("https://social.example/posts/1");
        schedule.UpdatedAt.Should().Be(postedAt);
        schedule.LastError.Should().BeNull();
        schedule.Invoking(x => x.MarkFailed(postedAt.AddHours(1), "facebook_http_400"))
            .Should().Throw<InvalidOperationException>();
        schedule.Invoking(x => x.Cancel(postedAt.AddHours(1)))
            .Should().Throw<InvalidOperationException>();
        schedule.Invoking(x => x.RecordRetry(postedAt.AddHours(1)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ContentSchedule_user_cancel_is_terminal_and_cannot_be_posted()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId,
            Guid.NewGuid(),
            1,
            "facebook",
            CreatedAt.AddHours(3),
            CreatedAt);
        var canceledAt = CreatedAt.AddHours(1);

        schedule.Cancel(canceledAt);

        schedule.Status.Should().Be(ContentSchedule.StatusCanceled);
        schedule.LastErrorCode.Should().Be(ContentSchedule.ErrorCanceledByUser);
        schedule.Invoking(x => x.MarkPublishing(canceledAt.AddMinutes(1)))
            .Should().Throw<InvalidOperationException>();
        schedule.Invoking(x => x.MarkPosted("https://social.example/posts/1", canceledAt.AddMinutes(1)))
            .Should().Throw<InvalidOperationException>();
        schedule.TryResetForRetry(canceledAt.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void ContentSchedule_mark_held_stays_pending_with_last_error()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), 1, "facebook", CreatedAt.AddHours(3), CreatedAt);
        var at = CreatedAt.AddHours(4);

        schedule.MarkHeld(ContentSchedule.ErrorHeldForReview, at);

        schedule.Status.Should().Be(ContentSchedule.StatusHeld);
        schedule.LastErrorCode.Should().Be(ContentSchedule.ErrorHeldForReview);
        schedule.LastError.Should().Be(ContentSchedule.ErrorHeldForReview);
        schedule.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void ContentSchedule_try_reset_for_retry_clears_failed_state()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId, Guid.NewGuid(), 1, "facebook", CreatedAt.AddHours(3), CreatedAt);
        schedule.MarkFailed(CreatedAt.AddHours(4), "publisher_down");
        // Force retry count to max via RecordRetry path first on a fresh schedule clone path:
        for (var i = 0; i < ContentSchedule.MaxRetries; i++)
            schedule.RecordRetry(CreatedAt.AddHours(5), "publisher_down");

        var ok = schedule.TryResetForRetry(CreatedAt.AddHours(6));

        ok.Should().BeTrue();
        schedule.Status.Should().Be(ContentSchedule.StatusPending);
        schedule.RetryCount.Should().Be(0);
        schedule.LastError.Should().BeNull();
    }

    [Fact]
    public void ContentItem_create_starts_revision_one_pending_review_without_approval()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);

        ReadRequiredProperty<int>(item, "ContentRevision").Should().Be(1);
        ReadRequiredProperty<string>(item, "AgentReviewStatus").Should().Be("pending");
        ReadRequiredPropertyValue(item, "AgentReviewedRevision").Should().BeNull();
        ReadRequiredPropertyValue(item, "ApprovedRevision").Should().BeNull();
    }

    [Fact]
    public void ContentItem_revise_body_increments_revision_and_invalidates_review_and_approval()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft v1", createdBy: null, CreatedAt);
        CompletePassedReviewAndAutomaticApproval(item);

        InvokeRequired(item, "ReviseBody", "Draft v2", CreatedAt.AddHours(1));

        item.Body.Should().Be("Draft v2");
        AssertCurrentRevisionIsPendingAndUnapproved(item, expectedRevision: 2);
    }

    [Fact]
    public void ContentItem_revise_assets_increments_revision_and_invalidates_review_and_approval()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        CompletePassedReviewAndAutomaticApproval(item);

        InvokeRequired(
            item,
            "ReviseAssets",
            "[{\"assetId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"}]",
            CreatedAt.AddHours(1));

        AssertCurrentRevisionIsPendingAndUnapproved(item, expectedRevision: 2);
        ReadRequiredProperty<string>(item, "ImageReviewStatus").Should().Be("pending");
    }

    [Fact]
    public void ContentItem_record_agent_review_rejects_stale_revision()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft v1", createdBy: null, CreatedAt);
        InvokeRequired(item, "BeginAgentReview", 1, CreatedAt.AddMinutes(1));
        InvokeRequired(item, "ReviseBody", "Draft v2", CreatedAt.AddMinutes(2));

        var act = () => InvokeRequired(
            item,
            "RecordAgentReview",
            1,
            "passed",
            "not_applicable",
            0,
            Guid.NewGuid(),
            "passed",
            CreatedAt.AddMinutes(3));

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeAssignableTo<InvalidOperationException>();
        AssertCurrentRevisionIsPendingAndUnapproved(item, expectedRevision: 2);
    }

    [Fact]
    public void ContentItem_record_agent_review_rejects_generator_self_review()
    {
        var generatorAgentId = Guid.NewGuid();
        var item = ContentItem.Create(
            TenantId,
            "facebook",
            "Draft",
            createdBy: null,
            CreatedAt,
            createdByAgentId: generatorAgentId);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));

        var act = () => item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: generatorAgentId,
            reason: "passed",
            at: CreatedAt.AddMinutes(2));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*content_reviewer_must_differ_from_generator*");
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
    }

    [Theory]
    [InlineData(ContentItem.ImageReviewStatusReviewed, 0)]
    [InlineData(ContentItem.ImageReviewStatusNotApplicable, 1)]
    [InlineData(ContentItem.ImageReviewStatusSkippedUnsupported, 1)]
    [InlineData(ContentItem.ImageReviewStatusFailed, 1)]
    public void ContentItem_record_agent_review_rejects_inconsistent_image_count(
        string imageStatus,
        int reviewedImageCount)
    {
        var item = ContentItem.Create(
            TenantId,
            "facebook",
            "Draft",
            createdBy: null,
            CreatedAt);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));

        var act = () => item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            imageStatus,
            reviewedImageCount,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            at: CreatedAt.AddMinutes(2));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*content_reviewed_image_count_invalid*");
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
    }

    [Fact]
    public void ContentItem_unattributed_reviewer_fallback_completes_current_review_for_human_override()
    {
        var generatorAgentId = Guid.NewGuid();
        var item = ContentItem.Create(
            TenantId,
            "facebook",
            "Draft",
            createdBy: null,
            CreatedAt,
            createdByAgentId: generatorAgentId);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));

        item.RecordUnattributedReviewFallback(
            item.ContentRevision,
            ContentItem.ImageReviewStatusNotApplicable,
            ContentItem.ReviewReasonReviewerIndependence,
            CreatedAt.AddMinutes(2));

        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusNeedsHuman);
        item.AgentReviewedRevision.Should().Be(item.ContentRevision);
        item.ReviewedByAgentId.Should().BeNull();
        item.AgentReviewReason.Should().Be("reviewer_independence");
        item.ImageReviewStatus.Should().Be(ContentItem.ImageReviewStatusNotApplicable);
        item.HumanApprovalRequirementReason.Should().Be("agent_non_pass");
    }

    [Theory]
    [InlineData(ContentItem.ImageReviewStatusReviewed, ContentItem.ReviewReasonReviewerIndependence)]
    [InlineData(ContentItem.ImageReviewStatusNotApplicable, "provider raw response")]
    public void ContentItem_unattributed_reviewer_fallback_rejects_non_fail_closed_input(
        string imageStatus,
        string reasonCode)
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));

        var act = () => item.RecordUnattributedReviewFallback(
            item.ContentRevision,
            imageStatus,
            reasonCode,
            CreatedAt.AddMinutes(2));

        act.Should().Throw<ArgumentException>();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        item.ReviewedByAgentId.Should().BeNull();
    }

    [Fact]
    public void ContentItem_human_override_of_non_pass_review_requires_reason()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        InvokeRequired(item, "BeginAgentReview", 1, CreatedAt.AddMinutes(1));
        InvokeRequired(
            item,
            "RecordAgentReview",
            1,
            "rejected",
            "not_applicable",
            0,
            Guid.NewGuid(),
            "unsupported claim",
            CreatedAt.AddMinutes(2));

        var act = () => InvokeRequired(
            item,
            "ApproveForPublishing",
            1,
            Guid.NewGuid(),
            "human_required",
            1L,
            " ",
            CreatedAt.AddMinutes(3));

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeAssignableTo<ArgumentException>();
        ReadRequiredPropertyValue(item, "ApprovedRevision").Should().BeNull();
    }

    [Fact]
    public void ContentItem_human_override_of_non_pass_review_records_reason_and_approver()
    {
        var approvedAt = CreatedAt.AddMinutes(3);
        var approverId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        InvokeRequired(item, "BeginAgentReview", 1, CreatedAt.AddMinutes(1));
        InvokeRequired(
            item,
            "RecordAgentReview",
            1,
            "needs_human",
            "not_applicable",
            0,
            reviewerId,
            "claim requires verification",
            CreatedAt.AddMinutes(2));

        InvokeRequired(
            item,
            "ApproveForPublishing",
            1,
            approverId,
            "human_required",
            7L,
            "  Đã xác minh thủ công  ",
            approvedAt);

        ReadRequiredProperty<int>(item, "ApprovedRevision").Should().Be(1);
        ReadRequiredProperty<string>(item, "ApprovalMode").Should().Be("human_override");
        ReadRequiredProperty<string>(item, "ApprovalReason").Should().Be("Đã xác minh thủ công");
        item.ApprovedBy.Should().Be(approverId);
        item.ApprovedAt.Should().Be(approvedAt);
        item.Status.Should().Be("approved");
    }

    [Theory]
    [InlineData(
        ContentItem.ReviewStatusPassed,
        ContentItem.ImageReviewStatusNotApplicable,
        ContentItem.PublishingPolicyAutomatic,
        null)]
    [InlineData(
        ContentItem.ReviewStatusPassed,
        ContentItem.ImageReviewStatusNotApplicable,
        ContentItem.PublishingPolicyHumanRequired,
        "tenant_policy")]
    [InlineData(
        ContentItem.ReviewStatusRejected,
        ContentItem.ImageReviewStatusNotApplicable,
        ContentItem.PublishingPolicyAutomatic,
        ContentItem.HumanApprovalReasonAgentNonPass)]
    [InlineData(
        ContentItem.ReviewStatusPassed,
        ContentItem.ImageReviewStatusFailed,
        ContentItem.PublishingPolicyAutomatic,
        ContentItem.HumanApprovalReasonAgentNonPass)]
    public void ContentItem_review_policy_snapshot_never_grants_publishing_approval(
        string reviewStatus,
        string imageStatus,
        string policy,
        string? expectedHumanRequirement)
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));
        item.RecordAgentReview(
            item.ContentRevision,
            reviewStatus,
            imageStatus,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: reviewStatus == ContentItem.ReviewStatusPassed ? "passed" : "agent_non_pass",
            at: CreatedAt.AddMinutes(2));

        item.RecordReviewPolicySnapshot(
            item.ContentRevision,
            policy,
            appliedPolicyVersion: 3,
            CreatedAt.AddMinutes(3));

        item.PublishingPolicyApplied.Should().Be(policy);
        item.PublishingPolicyVersionApplied.Should().Be(3);
        item.HumanApprovalRequirementReason.Should().Be(expectedHumanRequirement);
        item.Status.Should().Be("draft");
        item.ApprovedRevision.Should().BeNull();
        item.ApprovalMode.Should().BeNull();
        item.ApprovalReason.Should().BeNull();
        item.ApprovedBy.Should().BeNull();
        item.ApprovedByAgentId.Should().BeNull();
        item.ApprovedAt.Should().BeNull();
    }

    [Fact]
    public void ContentItem_record_review_policy_snapshot_preserves_migration_cutover_reason()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Cutover draft", createdBy: null, CreatedAt);
        item.RequireHumanApproval(
            ContentItem.HumanApprovalReasonMigrationCutover,
            CreatedAt.AddMinutes(1));
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(2));
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            CreatedAt.AddMinutes(3));

        item.RecordReviewPolicySnapshot(
            item.ContentRevision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 2,
            CreatedAt.AddMinutes(4));

        item.HumanApprovalRequirementReason
            .Should().Be(ContentItem.HumanApprovalReasonMigrationCutover);
        item.Status.Should().Be("draft");
        item.ApprovedRevision.Should().BeNull();
    }

    [Fact]
    public void ContentItem_approve_automatically_blocks_migration_cutover_items()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Cutover draft", createdBy: null, CreatedAt);
        item.RequireHumanApproval(
            ContentItem.HumanApprovalReasonMigrationCutover,
            CreatedAt.AddMinutes(1));
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(2));
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            CreatedAt.AddMinutes(3));
        item.RecordReviewPolicySnapshot(
            item.ContentRevision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 2,
            CreatedAt.AddMinutes(4));

        item.Invoking(value => value.ApproveAutomatically(
                item.ContentRevision,
                ContentItem.PublishingPolicyAutomatic,
                appliedPolicyVersion: 2,
                CreatedAt.AddMinutes(5)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_human_approval_required");
        item.ApprovedRevision.Should().BeNull();
        item.Status.Should().Be("draft");
        item.HumanApprovalRequirementReason
            .Should().Be(ContentItem.HumanApprovalReasonMigrationCutover);
    }

    [Fact]
    public void ContentItem_image_review_failure_requires_human_override_reason()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusFailed,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "image_decode_failed",
            at: CreatedAt.AddMinutes(2));

        var act = () => item.ApproveForPublishing(
            item.ContentRevision,
            Guid.NewGuid(),
            ContentItem.PublishingPolicyHumanRequired,
            appliedPolicyVersion: 7,
            overrideReason: null,
            at: CreatedAt.AddMinutes(3));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*content_override_reason_required*");
        item.ApprovedRevision.Should().BeNull();
    }

    [Fact]
    public void ContentItem_image_review_failure_records_human_override_with_reason()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusFailed,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "image_decode_failed",
            at: CreatedAt.AddMinutes(2));

        item.ApproveForPublishing(
            item.ContentRevision,
            Guid.NewGuid(),
            ContentItem.PublishingPolicyHumanRequired,
            appliedPolicyVersion: 7,
            overrideReason: "Đã kiểm tra ảnh thủ công",
            at: CreatedAt.AddMinutes(3));

        item.ApprovalMode.Should().Be(ContentItem.ApprovalModeHumanOverride);
        item.ApprovalReason.Should().Be("Đã kiểm tra ảnh thủ công");
        item.ApprovedRevision.Should().Be(item.ContentRevision);
    }

    [Fact]
    public void ContentItem_human_approval_of_passed_review_records_human_mode()
    {
        var approvedAt = CreatedAt.AddMinutes(3);
        var approverId = Guid.NewGuid();
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        InvokeRequired(item, "BeginAgentReview", 1, CreatedAt.AddMinutes(1));
        InvokeRequired(
            item,
            "RecordAgentReview",
            1,
            "passed",
            "not_applicable",
            0,
            Guid.NewGuid(),
            "passed",
            CreatedAt.AddMinutes(2));

        InvokeRequired(
            item,
            "ApproveForPublishing",
            1,
            approverId,
            "human_required",
            8L,
            null,
            approvedAt);

        ReadRequiredProperty<int>(item, "ApprovedRevision").Should().Be(1);
        ReadRequiredProperty<string>(item, "ApprovalMode").Should().Be("human");
        ReadRequiredPropertyValue(item, "ApprovalReason").Should().BeNull();
        item.ApprovedBy.Should().Be(approverId);
        item.ApprovedAt.Should().Be(approvedAt);
    }

    [Fact]
    public void ContentItem_final_human_rejection_blocks_same_revision_review_retry()
    {
        var rejectedAt = CreatedAt.AddMinutes(3);
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(1));
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            at: CreatedAt.AddMinutes(2));
        item.RejectForPublishing(
            item.ContentRevision,
            Guid.NewGuid(),
            "Không phù hợp chiến dịch",
            rejectedAt);

        var act = () => item.BeginAgentReview(item.ContentRevision, CreatedAt.AddMinutes(4));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_final_rejection_requires_new_revision*");
        item.Status.Should().Be("rejected");
        item.RejectedReason.Should().Be("Không phù hợp chiến dịch");
        item.UpdatedAt.Should().Be(rejectedAt);
    }

    [Fact]
    public void ContentItem_mark_published_always_requires_current_review_and_approval()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);

        var act = () => InvokeRequired(item, "MarkPublished", CreatedAt.AddHours(1));

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeAssignableTo<InvalidOperationException>();
        item.Status.Should().Be("draft");
    }

    [Fact]
    public void ContentItem_mark_published_rejects_current_review_without_publishing_approval()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        InvokeRequired(item, "BeginAgentReview", 1, CreatedAt.AddMinutes(1));
        InvokeRequired(
            item,
            "RecordAgentReview",
            1,
            "passed",
            "not_applicable",
            0,
            Guid.NewGuid(),
            "passed",
            CreatedAt.AddMinutes(2));

        var act = () => InvokeRequired(item, "MarkPublished", CreatedAt.AddHours(1));

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeAssignableTo<InvalidOperationException>();
        item.Status.Should().Be("draft");
    }

    [Fact]
    public void ContentItem_automatic_approval_rejects_item_without_current_passed_review()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);

        var act = () => InvokeRequired(
            item,
            "ApproveAutomatically",
            1,
            "automatic",
            1L,
            CreatedAt.AddMinutes(1));

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeAssignableTo<InvalidOperationException>();
        ReadRequiredPropertyValue(item, "ApprovedRevision").Should().BeNull();
    }

    [Fact]
    public void ContentItem_publish_claim_blocks_edit_until_definitive_release()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        CompletePassedReviewAndAutomaticApproval(item);
        item.MarkScheduled(CreatedAt.AddMinutes(4));
        var attemptId = Guid.NewGuid();

        item.ClaimPublishAttempt(item.ContentRevision, attemptId, CreatedAt.AddMinutes(5));

        item.ActivePublishAttemptId.Should().Be(attemptId);
        item.Invoking(x => x.ReviseBody("Changed", CreatedAt.AddMinutes(6)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*content_publish_attempt_active*");
        item.ReleasePublishAttempt(attemptId, CreatedAt.AddMinutes(7));
        item.ActivePublishAttemptId.Should().BeNull();
        item.Invoking(x => x.ReviseBody("Changed", CreatedAt.AddMinutes(8)))
            .Should().NotThrow();
    }

    [Fact]
    public void ContentItem_mark_published_accepts_matching_current_review_and_approval()
    {
        var publishedAt = CreatedAt.AddHours(1);
        var item = ContentItem.Create(TenantId, "facebook", "Draft", createdBy: null, CreatedAt);
        CompletePassedReviewAndAutomaticApproval(item);
        item.MarkScheduled(CreatedAt.AddMinutes(4));

        InvokeRequired(item, "MarkPublished", publishedAt);

        item.Status.Should().Be("published");
        item.UpdatedAt.Should().Be(publishedAt);
        ReadRequiredProperty<int>(item, "ContentRevision").Should().Be(1);
        ReadRequiredProperty<int>(item, "AgentReviewedRevision").Should().Be(1);
        ReadRequiredProperty<int>(item, "ApprovedRevision").Should().Be(1);
    }

    [Fact]
    public void ContentSchedule_schedule_requires_and_stores_content_revision()
    {
        var itemId = Guid.NewGuid();
        var schedule = InvokeStaticRequired(
            typeof(ContentSchedule),
            "Schedule",
            TenantId,
            itemId,
            3,
            "facebook",
            CreatedAt.AddHours(3),
            CreatedAt,
            Guid.NewGuid());

        schedule.Should().BeOfType<ContentSchedule>();
        ReadRequiredProperty<int>(schedule!, "ContentRevision").Should().Be(3);
    }

    [Fact]
    public void ContentSchedule_approval_context_is_write_once()
    {
        var schedule = ContentSchedule.Schedule(
            TenantId,
            Guid.NewGuid(),
            1,
            "facebook",
            CreatedAt.AddHours(3),
            CreatedAt);
        var publishTargetId = Guid.NewGuid();
        schedule.SetApprovalContext(ContentItem.ApprovalModeAutomatic, 4, publishTargetId);

        var act = () => schedule.SetApprovalContext(
            ContentItem.ApprovalModeHuman,
            5,
            Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_schedule_approval_context_already_set*");
        schedule.ApprovalMode.Should().Be(ContentItem.ApprovalModeAutomatic);
        schedule.PublishingPolicyVersionApplied.Should().Be(4);
        schedule.PublishTargetId.Should().Be(publishTargetId);
    }

    private static Action InvokeLegacyBridge(ContentItem item, string mutator) =>
        mutator switch
        {
            "Approve" => () => item.Approve(Guid.NewGuid(), CreatedAt.AddMinutes(10)),
            "ApproveByAgent" => () => item.ApproveByAgent(Guid.NewGuid(), CreatedAt.AddMinutes(10)),
            "Reject" => () => item.Reject(CreatedAt.AddMinutes(10), "legacy-reject"),
            "RevertToApproved" => () => item.RevertToApproved(CreatedAt.AddMinutes(10)),
            "AttachAgentSignoff" => () => item.AttachAgentSignoff(
                Guid.NewGuid(),
                CreatedAt.AddMinutes(10)),
            _ => throw new ArgumentOutOfRangeException(nameof(mutator), mutator, null),
        };

    private static void CompletePassedReviewAndAutomaticApproval(ContentItem item)
    {
        var reviewerId = Guid.NewGuid();
        InvokeRequired(item, "BeginAgentReview", 1, CreatedAt.AddMinutes(1));
        InvokeRequired(
            item,
            "RecordAgentReview",
            1,
            "passed",
            "not_applicable",
            0,
            reviewerId,
            "passed",
            CreatedAt.AddMinutes(2));
        InvokeRequired(
            item,
            "ApproveAutomatically",
            1,
            "automatic",
            1L,
            CreatedAt.AddMinutes(3));

        ReadRequiredProperty<int>(item, "ContentRevision").Should().Be(1);
        ReadRequiredProperty<string>(item, "AgentReviewStatus").Should().Be("passed");
        ReadRequiredProperty<int>(item, "AgentReviewedRevision").Should().Be(1);
        ReadRequiredProperty<Guid>(item, "ReviewedByAgentId").Should().Be(reviewerId);
        ReadRequiredProperty<int>(item, "ApprovedRevision").Should().Be(1);
        ReadRequiredProperty<string>(item, "ApprovalMode").Should().Be("automatic");
        ReadRequiredProperty<string>(item, "PublishingPolicyApplied").Should().Be("automatic");
        ReadRequiredProperty<long>(item, "PublishingPolicyVersionApplied").Should().Be(1L);
        item.ApprovedBy.Should().BeNull();
        item.Status.Should().Be("approved");
    }

    private static void AssertCurrentRevisionIsPendingAndUnapproved(
        ContentItem item,
        int expectedRevision)
    {
        ReadRequiredProperty<int>(item, "ContentRevision").Should().Be(expectedRevision);
        ReadRequiredProperty<string>(item, "AgentReviewStatus").Should().Be("pending");
        ReadRequiredPropertyValue(item, "AgentReviewedRevision").Should().BeNull();
        ReadRequiredPropertyValue(item, "ReviewedByAgentId").Should().BeNull();
        ReadRequiredPropertyValue(item, "AgentReviewedAt").Should().BeNull();
        ReadRequiredPropertyValue(item, "AgentReviewReason").Should().BeNull();
        ReadRequiredProperty<string>(item, "ImageReviewStatus").Should().Be("pending");
        ReadRequiredProperty<int>(item, "ReviewedImageCount").Should().Be(0);
        ReadRequiredPropertyValue(item, "PublishingPolicyApplied").Should().BeNull();
        ReadRequiredPropertyValue(item, "PublishingPolicyVersionApplied").Should().BeNull();
        ReadRequiredPropertyValue(item, "ApprovedRevision").Should().BeNull();
        ReadRequiredPropertyValue(item, "ApprovalMode").Should().BeNull();
        ReadRequiredPropertyValue(item, "ApprovalReason").Should().BeNull();
        item.ApprovedBy.Should().BeNull();
        item.ApprovedAt.Should().BeNull();
        item.Status.Should().Be("draft");
    }

    private static T ReadRequiredProperty<T>(object target, string propertyName)
    {
        var value = ReadRequiredPropertyValue(target, propertyName);
        value.Should().BeAssignableTo<T>();
        return (T)value!;
    }

    private static object? ReadRequiredPropertyValue(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        property.Should().NotBeNull($"{target.GetType().Name} must expose {propertyName}");
        return property!.GetValue(target);
    }

    private static object? InvokeRequired(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(candidate =>
                candidate.Name == methodName
                && candidate.GetParameters().Length == arguments.Length);
        method.Should().NotBeNull($"{target.GetType().Name} must expose {methodName}");

        return method!.Invoke(target, arguments);
    }

    private static object? InvokeStaticRequired(
        Type targetType,
        string methodName,
        params object?[] arguments)
    {
        var method = targetType
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .SingleOrDefault(candidate =>
                candidate.Name == methodName
                && candidate.GetParameters().Length == arguments.Length);
        method.Should().NotBeNull($"{targetType.Name} must expose {methodName}");

        return method!.Invoke(null, arguments);
    }
}
