using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentReviewTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ContentItemId = Guid.NewGuid();

    private static ContentReviewTask CreatePending() => ContentReviewTask.CreatePending(
        TenantId,
        ContentItemId,
        contentRevision: 1,
        nextAttemptAt: Now,
        createdAt: Now);

    [Fact]
    public void CreatePending_SetsInitialDefaults()
    {
        var task = CreatePending();

        task.Status.Should().Be(ContentReviewTask.StatusPending);
        task.AttemptCount.Should().Be(0);
        task.RefineAttemptCount.Should().Be(0);
        task.ReviewCycle.Should().Be(1);
        task.LeaseToken.Should().BeNull();
        task.StartedAt.Should().BeNull();
        task.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void CreatePending_ThrowsWhenIdentityEmpty()
    {
        var act = () => ContentReviewTask.CreatePending(Guid.Empty, ContentItemId, 1, Now, Now);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*content_review_task_identity_required*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreatePending_ThrowsWhenRevisionInvalid(int revision)
    {
        var act = () => ContentReviewTask.CreatePending(TenantId, ContentItemId, revision, Now, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Lease_TransitionsToLeasedAndIncrementsAttempt()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();

        task.Lease(lease, Now.AddMinutes(30), Now);

        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.LeaseToken.Should().Be(lease);
        task.AttemptCount.Should().Be(1);
        task.StartedAt.Should().Be(Now);
    }

    [Fact]
    public void Lease_ThrowsWhenNotPending()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        task.Complete(lease, Now.AddMinutes(1));

        var act = () => task.Lease(Guid.NewGuid(), Now.AddMinutes(60), Now.AddMinutes(2));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_not_pending*");
    }

    [Fact]
    public void Lease_ThrowsWhenNotDue()
    {
        var task = ContentReviewTask.CreatePending(
            TenantId, ContentItemId, 1, Now.AddHours(1), Now);

        var act = () => task.Lease(Guid.NewGuid(), Now.AddMinutes(30), Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_not_due*");
    }

    [Fact]
    public void ReclaimExpiredLease_RotatesTokenAndIncrementsAttempt()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(5), Now);
        var newLease = Guid.NewGuid();
        var afterExpiry = Now.AddMinutes(6);

        task.ReclaimExpiredLease(newLease, afterExpiry.AddMinutes(30), afterExpiry);

        task.LeaseToken.Should().Be(newLease);
        task.AttemptCount.Should().Be(2);
        task.LastErrorCode.Should().Be("lease_expired");
    }

    [Fact]
    public void ReclaimExpiredLease_ThrowsWhenSameToken()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(5), Now);

        var act = () => task.ReclaimExpiredLease(lease, Now.AddMinutes(30), Now.AddMinutes(6));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_lease_token_not_rotated*");
    }

    [Fact]
    public void TryClaimDelivery_ReturnsTrueOnFirstClaim()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);

        var claimed = task.TryClaimDelivery(lease, Now);

        claimed.Should().BeTrue();
        task.ClaimedLeaseToken.Should().Be(lease);
    }

    [Fact]
    public void TryClaimDelivery_ReturnsFalseOnSameToken()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        task.TryClaimDelivery(lease, Now);

        var claimed = task.TryClaimDelivery(lease, Now);

        claimed.Should().BeFalse();
    }

    [Fact]
    public void TryClaimDelivery_ThrowsOnDifferentToken()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        task.TryClaimDelivery(lease, Now);

        // EnsureActiveLease checks LeaseToken first — a different token hits lease_mismatch.
        var act = () => task.TryClaimDelivery(Guid.NewGuid(), Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_lease_mismatch*");
    }

    [Fact]
    public void RecordRefineAttempt_IncrementsOnce()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);

        task.RecordRefineAttempt(lease, Now);

        task.RefineAttemptCount.Should().Be(1);
    }

    [Fact]
    public void RecordRefineAttempt_ThrowsOnSecondCall()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        task.RecordRefineAttempt(lease, Now);

        var act = () => task.RecordRefineAttempt(lease, Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_refine_exhausted*");
    }

    [Fact]
    public void Complete_TransitionsToCompleted()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);

        task.Complete(lease, Now.AddMinutes(1));

        task.Status.Should().Be(ContentReviewTask.StatusCompleted);
        task.CompletedAt.Should().Be(Now.AddMinutes(1));
        task.LeaseToken.Should().BeNull();
    }

    [Fact]
    public void Fail_TransitionsToFailed()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);

        task.Fail(lease, "llm_error", Now.AddMinutes(1));

        task.Status.Should().Be(ContentReviewTask.StatusFailed);
        task.LastErrorCode.Should().Be("llm_error");
        task.CompletedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void ReleaseForRetry_ResetsToPending()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);

        task.ReleaseForRetry(lease, Now.AddMinutes(5), "transient", Now.AddMinutes(1));

        task.Status.Should().Be(ContentReviewTask.StatusPending);
        task.LeaseToken.Should().BeNull();
        task.NextAttemptAt.Should().Be(Now.AddMinutes(5));
        task.LastErrorCode.Should().Be("transient");
    }

    [Fact]
    public void DeferForOrchestrationStop_DecrementsAttemptAndResetsToPending()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);

        task.DeferForOrchestrationStop(lease, Now.AddMinutes(10), Now.AddMinutes(1));

        task.Status.Should().Be(ContentReviewTask.StatusPending);
        task.AttemptCount.Should().Be(0); // was 1, decremented
        task.LastErrorCode.Should().Be("orchestration_session_stopped");
    }

    [Fact]
    public void DeferForOrchestrationStop_DoesNotDecrementBelowZero()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        // AttemptCount is 1 after Lease. Defer decrements to 0.
        task.DeferForOrchestrationStop(lease, Now.AddMinutes(10), Now);
        task.AttemptCount.Should().Be(0);

        // Re-lease at a later time (NextAttemptAt was set to Now+10min by defer).
        var reLeaseTime = Now.AddMinutes(11);
        task.Lease(lease, reLeaseTime.AddMinutes(30), reLeaseTime);
        // AttemptCount is 1 again. Defer should bring it to 0, not -1.
        task.DeferForOrchestrationStop(lease, reLeaseTime.AddMinutes(10), reLeaseTime);

        task.AttemptCount.Should().Be(0);
    }

    [Fact]
    public void FailExhausted_TerminalizesWhenCapReached()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        // Drive attempt count up via repeated lease/release cycles.
        for (var i = 0; i < 5; i++)
        {
            task.Lease(lease, Now.AddMinutes(30), Now.AddMinutes(i));
            task.ReleaseForRetry(lease, Now.AddMinutes(i + 1), "error", Now.AddMinutes(i));
        }
        // AttemptCount is 5 after 5 leases. Expire the last lease.
        var afterExpiry = Now.AddMinutes(35);

        task.FailExhausted(maxAttempts: 5, afterExpiry);

        task.Status.Should().Be(ContentReviewTask.StatusFailed);
        task.LastErrorCode.Should().Be("content_review_attempt_limit_reached");
    }

    [Fact]
    public void FailExhausted_ThrowsWhenCapNotReached()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        task.ReleaseForRetry(lease, Now.AddMinutes(1), "error", Now);
        // AttemptCount is 1.

        var act = () => task.FailExhausted(maxAttempts: 5, Now.AddMinutes(35));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_attempt_limit_not_reached*");
    }

    [Fact]
    public void CancelStale_TransitionsFromAnyNonTerminal()
    {
        var task = CreatePending();

        task.CancelStale(Now);

        task.Status.Should().Be(ContentReviewTask.StatusCanceledStale);
        task.LastErrorCode.Should().Be("stale_content_revision");
    }

    [Fact]
    public void CancelStale_ThrowsWhenAlreadyTerminal()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        task.Complete(lease, Now.AddMinutes(1));

        var act = () => task.CancelStale(Now.AddMinutes(2));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_terminal*");
    }

    [Fact]
    public void ReopenForManualRetry_ResetsTerminalToPending()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);
        task.Complete(lease, Now.AddMinutes(1));

        task.ReopenForManualRetry(Now.AddMinutes(2));

        task.Status.Should().Be(ContentReviewTask.StatusPending);
        task.AttemptCount.Should().Be(0);
        task.ReviewCycle.Should().Be(2);
        task.StartedAt.Should().BeNull();
        task.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void ReopenForManualRetry_ThrowsWhenNotTerminal()
    {
        var task = CreatePending();

        var act = () => task.ReopenForManualRetry(Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_not_terminal*");
    }

    [Fact]
    public void CancelForOrchestrationFailure_SetsCorrectErrorCode()
    {
        var task = CreatePending();

        task.CancelForOrchestrationFailure(Now);

        task.Status.Should().Be(ContentReviewTask.StatusCanceledStale);
        task.LastErrorCode.Should().Be("orchestration_plan_failed");
    }

    [Fact]
    public void DeferExhaustedForOrchestrationStop_WorksOnExpiredLease()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(5), Now);
        var afterExpiry = Now.AddMinutes(6);

        task.DeferExhaustedForOrchestrationStop(afterExpiry.AddMinutes(10), afterExpiry);

        task.Status.Should().Be(ContentReviewTask.StatusPending);
        task.AttemptCount.Should().Be(0); // was 1, decremented
        task.LastErrorCode.Should().Be("orchestration_session_stopped");
    }

    [Fact]
    public void DeferExhaustedForOrchestrationStop_ThrowsOnActiveLease()
    {
        var task = CreatePending();
        var lease = Guid.NewGuid();
        task.Lease(lease, Now.AddMinutes(30), Now);

        var act = () => task.DeferExhaustedForOrchestrationStop(Now.AddMinutes(10), Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*content_review_task_lease_active*");
    }
}
