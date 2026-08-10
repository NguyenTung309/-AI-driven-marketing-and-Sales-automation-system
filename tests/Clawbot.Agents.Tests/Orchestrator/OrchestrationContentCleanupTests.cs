using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class OrchestrationContentCleanupTests
{
    [Fact]
    public void RejectForOrchestrationFailure_RejectsUnclaimedApprovedDraft()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(
            tenantId,
            "facebook",
            "Draft body",
            createdBy: null,
            at,
            orchestrationSessionId: sessionId,
            orchestrationPlanGeneration: 0);
        item.Approve(Guid.NewGuid(), at);

        item.RejectForOrchestrationFailure(sessionId, 0, at.AddMinutes(1));

        item.Status.Should().Be("rejected");
        item.RejectedReason.Should().Be("orchestration_plan_failed");
    }

    [Fact]
    public void RejectForOrchestrationFailure_PreservesHumanClaimedDraft()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(
            tenantId,
            "facebook",
            "Draft body",
            createdBy: null,
            at,
            orchestrationSessionId: sessionId,
            orchestrationPlanGeneration: 0);
        item.ClaimOrchestrationOwnershipForHuman(Guid.NewGuid(), at.AddMinutes(1));

        var action = () => item.RejectForOrchestrationFailure(sessionId, 0, at.AddMinutes(2));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("content_orchestration_ownership_claimed");
    }

    [Fact]
    public void DeferAgentReviewForOrchestrationStop_RefundsUnfinishedReviewAttempt()
    {
        var at = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(
            Guid.NewGuid(),
            "facebook",
            "Draft body",
            createdBy: null,
            at,
            orchestrationSessionId: Guid.NewGuid(),
            orchestrationPlanGeneration: 0);
        item.BeginAgentReview(item.ContentRevision, at);

        item.DeferAgentReviewForOrchestrationStop(item.ContentRevision, at.AddMinutes(1));

        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPending);
        item.AgentReviewAttemptCount.Should().Be(0);
        item.AgentReviewStartedAt.Should().BeNull();
        item.AgentReviewedAt.Should().BeNull();
        item.AgentReviewedRevision.Should().BeNull();
        item.ReviewedByAgentId.Should().BeNull();
        item.AgentReviewReason.Should().BeNull();
        item.ImageReviewStatus.Should().Be(ContentItem.ImageReviewStatusPending);
        item.ReviewedImageCount.Should().Be(0);
    }

    [Fact]
    public void ClaimOrchestrationOwnershipForHuman_UnschedulesPendingPublication()
    {
        var at = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(
            Guid.NewGuid(),
            "facebook",
            "Draft body",
            createdBy: null,
            at,
            orchestrationSessionId: Guid.NewGuid(),
            orchestrationPlanGeneration: 0);
        item.BeginAgentReview(item.ContentRevision, at);
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            Guid.NewGuid(),
            reason: null,
            at);
        item.RecordReviewPolicySnapshot(
            item.ContentRevision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            at);
        item.ApproveAutomatically(
            item.ContentRevision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            at);
        item.MarkScheduled(at.AddMinutes(1));

        item.ClaimOrchestrationOwnershipForHuman(Guid.NewGuid(), at.AddMinutes(2));

        item.Status.Should().Be("draft");
        item.OrchestrationOwnershipClaimedAt.Should().Be(at.AddMinutes(2));
        item.CanPublishCurrentRevision().Should().BeFalse();
    }

    [Fact]
    public void SoftDelete_RejectsActivePublishAttempt()
    {
        var at = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(Guid.NewGuid(), "facebook", "Draft body", null, at);
        item.BeginAgentReview(item.ContentRevision, at);
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            Guid.NewGuid(),
            reason: null,
            at);
        item.RecordReviewPolicySnapshot(
            item.ContentRevision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            at);
        item.ApproveAutomatically(
            item.ContentRevision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            at);
        item.MarkScheduled(at.AddMinutes(1));
        item.ClaimPublishAttempt(item.ContentRevision, Guid.NewGuid(), at.AddMinutes(2));

        var action = () => item.SoftDelete(at.AddMinutes(3));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("content_publish_attempt_active");
    }
}
