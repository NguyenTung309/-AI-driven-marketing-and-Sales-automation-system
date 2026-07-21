using Clawbot.Domain.Common;
using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentDurableEntitiesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReviewTask_create_sets_pending_defaults_and_scope()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var task = ContentReviewTask.CreatePending(
            tenantId,
            itemId,
            contentRevision: 2,
            nextAttemptAt: Now,
            createdAt: Now);

        task.Id.Should().NotBeEmpty();
        task.TenantId.Should().Be(tenantId);
        task.ContentItemId.Should().Be(itemId);
        task.ContentRevision.Should().Be(2);
        task.Status.Should().Be(ContentReviewTask.StatusPending);
        task.AttemptCount.Should().Be(0);
        task.NextAttemptAt.Should().Be(Now);
        task.LeaseToken.Should().BeNull();
        task.LeaseExpiresAt.Should().BeNull();
        task.StartedAt.Should().BeNull();
        task.CompletedAt.Should().BeNull();
        task.LastErrorCode.Should().BeNull();
        task.ClaimedLeaseToken.Should().BeNull();
        task.Should().BeAssignableTo<ITenantOwned>();
        task.Should().BeAssignableTo<IAuditExempt>();
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 0)]
    [InlineData(false, false, -1)]
    public void ReviewTask_create_rejects_invalid_scope(
        bool emptyTenant,
        bool emptyItem,
        int revision)
    {
        var act = () => ContentReviewTask.CreatePending(
            emptyTenant ? Guid.Empty : Guid.NewGuid(),
            emptyItem ? Guid.Empty : Guid.NewGuid(),
            revision,
            Now,
            Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReviewTask_requires_current_lease_token_for_completion()
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);
        var leaseToken = Guid.NewGuid();
        task.Lease(leaseToken, Now.AddMinutes(5), Now);

        var staleCompletion = () => task.Complete(Guid.NewGuid(), Now.AddMinutes(1));

        staleCompletion.Should().Throw<InvalidOperationException>()
            .WithMessage("content_review_task_lease_mismatch");
        task.Status.Should().Be(ContentReviewTask.StatusLeased);

        task.Complete(leaseToken, Now.AddMinutes(2));
        task.Status.Should().Be(ContentReviewTask.StatusCompleted);
        task.CompletedAt.Should().Be(Now.AddMinutes(2));
        task.LeaseToken.Should().BeNull();
        task.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public void ReviewTask_reclaims_only_an_expired_lease_with_a_new_token()
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);
        var originalToken = Guid.NewGuid();
        var replacementToken = Guid.NewGuid();
        task.Lease(originalToken, Now.AddMinutes(5), Now);

        task.Invoking(value => value.ReclaimExpiredLease(
                replacementToken,
                Now.AddMinutes(10),
                Now.AddMinutes(4)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_review_task_lease_not_expired");

        task.ReclaimExpiredLease(
            replacementToken,
            Now.AddMinutes(11),
            Now.AddMinutes(6));

        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.LeaseToken.Should().Be(replacementToken);
        task.LeaseExpiresAt.Should().Be(Now.AddMinutes(11));
        task.AttemptCount.Should().Be(2);
        task.Invoking(value => value.Complete(originalToken, Now.AddMinutes(7)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_review_task_lease_mismatch");
    }

    [Fact]
    public void ReviewTask_claims_each_lease_once_and_allows_replacement_claim()
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);
        var originalToken = Guid.NewGuid();
        var replacementToken = Guid.NewGuid();
        task.Lease(originalToken, Now.AddMinutes(5), Now);

        task.TryClaimDelivery(originalToken, Now.AddMinutes(1)).Should().BeTrue();
        task.ClaimedLeaseToken.Should().Be(originalToken);
        task.TryClaimDelivery(originalToken, Now.AddMinutes(2)).Should().BeFalse();

        task.ReclaimExpiredLease(
            replacementToken,
            Now.AddMinutes(11),
            Now.AddMinutes(6));
        task.ClaimedLeaseToken.Should().BeNull();
        task.TryClaimDelivery(replacementToken, Now.AddMinutes(7)).Should().BeTrue();
        task.ClaimedLeaseToken.Should().Be(replacementToken);
    }

    [Fact]
    public void ReviewTask_terminal_transition_clears_delivery_claim()
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);
        var leaseToken = Guid.NewGuid();
        task.Lease(leaseToken, Now.AddMinutes(5), Now);
        task.TryClaimDelivery(leaseToken, Now.AddMinutes(1));

        task.Complete(leaseToken, Now.AddMinutes(2));

        task.ClaimedLeaseToken.Should().BeNull();
    }

    [Fact]
    public void ReviewTask_reclaims_lease_at_exact_expiry()
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);
        var originalToken = Guid.NewGuid();
        var replacementToken = Guid.NewGuid();
        var expiresAt = Now.AddMinutes(5);
        task.Lease(originalToken, expiresAt, Now);

        task.Invoking(value => value.ReclaimExpiredLease(
                replacementToken,
                expiresAt.AddMinutes(5),
                expiresAt.AddTicks(-1)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_review_task_lease_not_expired");

        task.ReclaimExpiredLease(
            replacementToken,
            expiresAt.AddMinutes(5),
            expiresAt);

        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.LeaseToken.Should().Be(replacementToken);
        task.ClaimedLeaseToken.Should().BeNull();
        task.LeaseExpiresAt.Should().Be(expiresAt.AddMinutes(5));
        task.AttemptCount.Should().Be(2);
        task.LastErrorCode.Should().Be("lease_expired");
    }

    [Fact]
    public void ReviewTask_fail_exhausted_accepts_pending_or_expired_lease_at_limit()
    {
        var pending = CreateReviewTaskWithReleasedAttempts(
            ContentItem.MaxAgentReviewAttempts);
        var exhaustedAt = Now.AddMinutes(ContentItem.MaxAgentReviewAttempts);

        pending.FailExhausted(ContentItem.MaxAgentReviewAttempts, exhaustedAt);

        pending.Status.Should().Be(ContentReviewTask.StatusFailed);
        pending.LastErrorCode.Should().Be("content_review_attempt_limit_reached");
        pending.LeaseToken.Should().BeNull();
        pending.ClaimedLeaseToken.Should().BeNull();
        pending.LeaseExpiresAt.Should().BeNull();
        pending.CompletedAt.Should().Be(exhaustedAt);

        var expired = CreateReviewTaskWithExpiredLeaseAttempts(
            ContentItem.MaxAgentReviewAttempts);
        var expiredAt = expired.LeaseExpiresAt!.Value;

        expired.FailExhausted(ContentItem.MaxAgentReviewAttempts, expiredAt);

        expired.Status.Should().Be(ContentReviewTask.StatusFailed);
        expired.LastErrorCode.Should().Be("content_review_attempt_limit_reached");
        expired.LeaseToken.Should().BeNull();
    }

    [Fact]
    public void ReviewTask_fail_exhausted_rejects_active_unexpired_lease()
    {
        var task = CreateReviewTaskWithExpiredLeaseAttempts(
            ContentItem.MaxAgentReviewAttempts);
        var activeAt = task.LeaseExpiresAt!.Value.AddTicks(-1);

        task.Invoking(value => value.FailExhausted(
                ContentItem.MaxAgentReviewAttempts,
                activeAt))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_review_task_lease_active");
    }

    [Fact]
    public void ReviewTask_fail_exhausted_rejects_attempt_count_below_limit()
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);

        task.Invoking(value => value.FailExhausted(
                ContentItem.MaxAgentReviewAttempts,
                Now))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_review_task_attempt_limit_not_reached");
    }

    [Fact]
    public void Asset_reserve_generates_server_owned_key_and_ready_metadata()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var asset = ContentAsset.Reserve(
            tenantId,
            itemId,
            "../campaign/banner.png",
            sortOrder: 3,
            createdAt: Now);
        var sha256 = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

        asset.StorageKey.Should().Be(
            $"tenants/{tenantId:N}/content/{itemId:N}/{asset.Id:N}");
        asset.OriginalFileName.Should().Be("banner.png");
        asset.Status.Should().Be(ContentAsset.StatusUploading);
        asset.Should().BeAssignableTo<IAuditExempt>();

        asset.MarkReady(sha256, sizeBytes: 512, "image/png", Now.AddMinutes(1));

        asset.Status.Should().Be(ContentAsset.StatusReady);
        asset.Sha256.Should().Equal(sha256);
        asset.SizeBytes.Should().Be(512);
        asset.ContentType.Should().Be("image/png");
        asset.ReadyAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Asset_invalid_ready_metadata_does_not_mutate_upload()
    {
        var asset = ContentAsset.Reserve(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "banner.png",
            sortOrder: 0,
            createdAt: Now);

        var act = () => asset.MarkReady(new byte[31], 0, "", Now.AddMinutes(1));

        act.Should().Throw<ArgumentException>();
        asset.Status.Should().Be(ContentAsset.StatusUploading);
        asset.Sha256.Should().BeNull();
        asset.SizeBytes.Should().BeNull();
        asset.ContentType.Should().BeNull();
        asset.ReadyAt.Should().BeNull();
    }

    [Fact]
    public void Asset_deletion_has_unambiguous_terminal_state()
    {
        var asset = ContentAsset.Reserve(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "banner.png",
            sortOrder: 0,
            createdAt: Now);
        asset.MarkDeletePending("asset_removed", Now.AddMinutes(1));

        asset.MarkDeleted(Now.AddMinutes(2));

        asset.Status.Should().Be(ContentAsset.StatusDeleted);
        asset.DeletedAt.Should().Be(Now.AddMinutes(2));
        asset.Invoking(value => value.MarkReady(new byte[32], 1, "image/png", Now.AddMinutes(3)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PublishAttempt_claim_freezes_snapshot_and_server_generated_identity()
    {
        var tenantId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        const string body = "  exact body  ";
        var assets = new[]
        {
            new ContentPublishAssetSnapshot(
                assetId,
                new string('a', 64),
                SortOrder: 0,
                ContentType: "image/png",
                SizeBytes: 128),
        };

        var attempt = ContentPublishAttempt.Claim(
            tenantId,
            scheduleId,
            itemId,
            contentRevision: 4,
            platform: "facebook",
            publishTargetId: targetId,
            bodySnapshot: body,
            assetSnapshots: assets,
            leaseExpiresAt: Now.AddMinutes(5),
            claimedAt: Now);

        attempt.Id.Should().NotBeEmpty();
        attempt.AttemptToken.Should().NotBeEmpty();
        attempt.LeaseToken.Should().Be(attempt.AttemptToken);
        attempt.IdempotencyKey.Should().Be(
            $"content-publish:{tenantId:N}:{scheduleId:N}:4:{targetId:N}");
        attempt.Status.Should().Be(ContentPublishAttempt.StatusClaimed);
        attempt.BodySnapshot.Should().Be(body);
        attempt.AssetsSnapshotJson.Should().Contain(assetId.ToString());
        attempt.AssetsSnapshotJson.Should().NotContain("storageKey");
        attempt.AssetsSnapshotJson.Should().NotContain("url");
        attempt.SnapshotSha256.Should().HaveCount(32);
        attempt.SnapshotSchemaVersion.Should().Be(1);
        attempt.LeaseExpiresAt.Should().Be(Now.AddMinutes(5));
        attempt.Should().BeAssignableTo<IAuditExempt>();
    }

    [Fact]
    public void PublishAttempt_reclaims_expired_untransmitted_claim_with_stable_identity()
    {
        var attempt = ContentPublishAttempt.Claim(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            platform: "facebook",
            publishTargetId: Guid.NewGuid(),
            bodySnapshot: "body",
            assetSnapshots: Array.Empty<ContentPublishAssetSnapshot>(),
            leaseExpiresAt: Now.AddMinutes(5),
            claimedAt: Now);
        var originalToken = attempt.LeaseToken!.Value;
        var replacementToken = Guid.NewGuid();
        var attemptToken = attempt.AttemptToken;
        var idempotencyKey = attempt.IdempotencyKey;

        attempt.ReclaimExpiredClaim(
            replacementToken,
            Now.AddMinutes(11),
            Now.AddMinutes(6));

        attempt.Status.Should().Be(ContentPublishAttempt.StatusClaimed);
        attempt.LeaseToken.Should().Be(replacementToken);
        attempt.LeaseExpiresAt.Should().Be(Now.AddMinutes(11));
        attempt.AttemptToken.Should().Be(attemptToken);
        attempt.IdempotencyKey.Should().Be(idempotencyKey);
        attempt.Invoking(value => value.MarkTransmitted(
                originalToken,
                providerRequestId: null,
                Now.AddMinutes(7)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_publish_attempt_token_mismatch");
    }

    [Fact]
    public void PublishAttempt_expired_transmission_requires_reconciliation()
    {
        var attempt = ContentPublishAttempt.Claim(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            platform: "facebook",
            publishTargetId: Guid.NewGuid(),
            bodySnapshot: "body",
            assetSnapshots: Array.Empty<ContentPublishAssetSnapshot>(),
            leaseExpiresAt: Now.AddMinutes(5),
            claimedAt: Now);
        attempt.MarkTransmitted(attempt.LeaseToken!.Value, "request-1", Now.AddMinutes(1));

        attempt.MarkExpiredTransmissionOutcomeUnknown(
            "publisher_lease_expired",
            Now.AddMinutes(6));

        attempt.Status.Should().Be(ContentPublishAttempt.StatusOutcomeUnknown);
        attempt.LeaseToken.Should().BeNull();
        attempt.Invoking(value => value.ReclaimExpiredClaim(
                Guid.NewGuid(),
                Now.AddMinutes(12),
                Now.AddMinutes(7)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("content_publish_attempt_not_claimed");

        attempt.ReconcileSucceeded("post-1", Now.AddMinutes(8));
        attempt.Status.Should().Be(ContentPublishAttempt.StatusReconciled);
        attempt.ExternalPostId.Should().Be("post-1");
        attempt.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void PublishAttempt_rejects_stale_token_and_unknown_outcome_is_not_retryable()
    {
        var attempt = ContentPublishAttempt.Claim(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            platform: "facebook",
            publishTargetId: Guid.NewGuid(),
            bodySnapshot: "body",
            assetSnapshots: Array.Empty<ContentPublishAssetSnapshot>(),
            leaseExpiresAt: Now.AddMinutes(5),
            claimedAt: Now);

        var staleTransmit = () => attempt.MarkTransmitted(
            Guid.NewGuid(),
            providerRequestId: "request-1",
            at: Now.AddMinutes(1));
        staleTransmit.Should().Throw<InvalidOperationException>()
            .WithMessage("content_publish_attempt_token_mismatch");

        attempt.MarkTransmitted(attempt.LeaseToken!.Value, "request-1", Now.AddMinutes(1));
        attempt.MarkOutcomeUnknown(attempt.LeaseToken!.Value, "publisher_timeout", Now.AddMinutes(2));

        attempt.Status.Should().Be(ContentPublishAttempt.StatusOutcomeUnknown);
        attempt.CompletedAt.Should().Be(Now.AddMinutes(2));
        attempt.Invoking(value => value.MarkFailed(
                value.AttemptToken,
                "publisher_failed",
                Now.AddMinutes(3)))
            .Should().Throw<InvalidOperationException>();
    }

    private static ContentReviewTask CreateReviewTaskWithReleasedAttempts(int attemptCount)
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var leaseAt = Now.AddMinutes(attempt);
            var token = Guid.NewGuid();
            task.Lease(token, leaseAt.AddMinutes(5), leaseAt);
            task.ReleaseForRetry(
                token,
                leaseAt.AddMinutes(1),
                "reviewer_error",
                leaseAt.AddMinutes(1));
        }

        return task;
    }

    private static ContentReviewTask CreateReviewTaskWithExpiredLeaseAttempts(int attemptCount)
    {
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            contentRevision: 1,
            nextAttemptAt: Now,
            createdAt: Now);

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var token = Guid.NewGuid();
            if (task.Status == ContentReviewTask.StatusLeased)
            {
                var reclaimAt = task.LeaseExpiresAt!.Value;
                task.ReclaimExpiredLease(
                    token,
                    reclaimAt.AddMinutes(5),
                    reclaimAt);
            }
            else
            {
                var leaseAt = Now.AddMinutes(attempt);
                task.Lease(token, leaseAt.AddMinutes(5), leaseAt);
            }
        }

        return task;
    }
}
