using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

// Máy trạng thái lần đăng bài: claimed -> transmitted -> succeeded/failed/outcome_unknown -> reconciled.
public sealed class ContentPublishAttemptTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Schedule = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Item = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Target = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    private static ContentPublishAttempt Claim()
    {
        return ContentPublishAttempt.Claim(
            Tenant, Schedule, Item, contentRevision: 1, platform: "Facebook",
            publishTargetId: Target, bodySnapshot: "Nội dung bài đăng",
            assetSnapshots: [],
            leaseExpiresAt: Now.AddMinutes(5), claimedAt: Now);
    }

    [Fact]
    public void Claim_Valid_SetsClaimedStateAndSnapshot()
    {
        var attempt = Claim();

        attempt.Status.Should().Be(ContentPublishAttempt.StatusClaimed);
        attempt.Platform.Should().Be("facebook"); // lowercased
        attempt.LeaseToken.Should().Be(attempt.AttemptToken);
        attempt.IdempotencyKey.Should().StartWith("content-publish:");
        attempt.SnapshotSha256.Should().NotBeEmpty();
    }

    [Fact]
    public void Claim_AttemptSequence2_DifferentIdempotencyKey()
    {
        var first = Claim();
        var second = ContentPublishAttempt.Claim(
            Tenant, Schedule, Item, 1, "facebook", Target, "b", [],
            Now.AddMinutes(5), Now, attemptSequence: 2);

        second.IdempotencyKey.Should().NotBe(first.IdempotencyKey);
        second.IdempotencyKey.Should().EndWith(":2");
    }

    [Fact]
    public void Claim_BlankBody_Throws()
    {
        var act = () => ContentPublishAttempt.Claim(
            Tenant, Schedule, Item, 1, "facebook", Target, "  ", [], Now.AddMinutes(5), Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Claim_LeaseExpiryNotAfterClaim_Throws()
    {
        var act = () => ContentPublishAttempt.Claim(
            Tenant, Schedule, Item, 1, "facebook", Target, "b", [], Now, Now);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Claim_WithAssets_SerializesAndNormalizes()
    {
        var asset = new ContentPublishAssetSnapshot(
            Guid.NewGuid(), new string('A', 64), SortOrder: 0, ContentType: "IMAGE/PNG", SizeBytes: 1024);

        var attempt = ContentPublishAttempt.Claim(
            Tenant, Schedule, Item, 1, "facebook", Target, "b", [asset], Now.AddMinutes(5), Now);

        attempt.AssetsSnapshotJson.Should().Contain("image/png"); // content type lowercased
    }

    [Fact]
    public void Claim_DuplicateAssetSortOrder_Throws()
    {
        var a1 = new ContentPublishAssetSnapshot(Guid.NewGuid(), new string('a', 64), 0, "image/png", 10);
        var a2 = new ContentPublishAssetSnapshot(Guid.NewGuid(), new string('b', 64), 0, "image/png", 10);

        var act = () => ContentPublishAttempt.Claim(
            Tenant, Schedule, Item, 1, "facebook", Target, "b", [a1, a2], Now.AddMinutes(5), Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Claim_AssetInvalidHash_Throws()
    {
        var bad = new ContentPublishAssetSnapshot(Guid.NewGuid(), "short", 0, "image/png", 10);

        var act = () => ContentPublishAttempt.Claim(
            Tenant, Schedule, Item, 1, "facebook", Target, "b", [bad], Now.AddMinutes(5), Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkTransmitted_ThenSucceeded_HappyPath()
    {
        var attempt = Claim();
        var token = attempt.LeaseToken!.Value;

        attempt.MarkTransmitted(token, "provider-req-1", Now.AddSeconds(10));
        attempt.Status.Should().Be(ContentPublishAttempt.StatusTransmitted);
        attempt.ProviderRequestId.Should().Be("provider-req-1");

        attempt.MarkSucceeded(token, "ext-post-99", Now.AddSeconds(20));
        attempt.Status.Should().Be(ContentPublishAttempt.StatusSucceeded);
        attempt.ExternalPostId.Should().Be("ext-post-99");
        attempt.HasConfirmedPublication().Should().BeTrue();
        attempt.LeaseToken.Should().BeNull();
    }

    [Fact]
    public void MarkSucceeded_TokenMismatch_Throws()
    {
        var attempt = Claim();
        var act = () => attempt.MarkSucceeded(Guid.NewGuid(), "ext", Now.AddSeconds(10));
        act.Should().Throw<InvalidOperationException>().WithMessage("*token_mismatch*");
    }

    [Fact]
    public void MarkFailed_FromClaimed_TransitionsToFailedAndReopenable()
    {
        var attempt = Claim();
        var token = attempt.LeaseToken!.Value;

        attempt.MarkFailed(token, "provider_rejected", Now.AddSeconds(10));

        attempt.Status.Should().Be(ContentPublishAttempt.StatusFailed);
        attempt.LastErrorCode.Should().Be("provider_rejected");
        attempt.CanReopenForRetry().Should().BeTrue();
        attempt.HasConfirmedPublication().Should().BeFalse();
    }

    [Fact]
    public void ReopenForRetry_FromFailed_ReturnsToClaimed()
    {
        var attempt = Claim();
        var token = attempt.LeaseToken!.Value;
        attempt.MarkFailed(token, "err", Now.AddSeconds(10));

        var newToken = Guid.NewGuid();
        attempt.ReopenForRetry(newToken, Now.AddMinutes(10), Now.AddMinutes(1));

        attempt.Status.Should().Be(ContentPublishAttempt.StatusClaimed);
        attempt.LeaseToken.Should().Be(newToken);
        attempt.ProviderRequestId.Should().BeNull();
    }

    [Fact]
    public void ReopenForRetry_WhenNotReopenable_Throws()
    {
        var attempt = Claim(); // still claimed, not failed
        var act = () => attempt.ReopenForRetry(Guid.NewGuid(), Now.AddMinutes(10), Now.AddMinutes(1));
        act.Should().Throw<InvalidOperationException>().WithMessage("*not_reopenable*");
    }

    [Fact]
    public void MarkOutcomeUnknown_ThenReconcileSucceeded_ConfirmsPublication()
    {
        var attempt = Claim();
        var token = attempt.LeaseToken!.Value;
        attempt.MarkTransmitted(token, "req", Now.AddSeconds(10));

        attempt.MarkOutcomeUnknown(token, "timeout", Now.AddSeconds(20));
        attempt.Status.Should().Be(ContentPublishAttempt.StatusOutcomeUnknown);

        attempt.ReconcileSucceeded("ext-123", Now.AddSeconds(30));
        attempt.Status.Should().Be(ContentPublishAttempt.StatusReconciled);
        attempt.HasConfirmedPublication().Should().BeTrue();
        attempt.CanReopenForRetry().Should().BeFalse();
    }

    [Fact]
    public void ReconcileFailed_FromOutcomeUnknown_Reopenable()
    {
        var attempt = Claim();
        var token = attempt.LeaseToken!.Value;
        attempt.MarkTransmitted(token, "req", Now.AddSeconds(10));
        attempt.MarkOutcomeUnknown(token, "timeout", Now.AddSeconds(20));

        attempt.ReconcileFailed("confirmed_not_posted", Now.AddSeconds(30));

        attempt.Status.Should().Be(ContentPublishAttempt.StatusReconciled);
        attempt.ExternalPostId.Should().BeNull();
        attempt.CanReopenForRetry().Should().BeTrue();
    }

    [Fact]
    public void ReconcileSucceeded_WhenNotOutcomeUnknown_Throws()
    {
        var attempt = Claim();
        var act = () => attempt.ReconcileSucceeded("ext", Now.AddSeconds(10));
        act.Should().Throw<InvalidOperationException>().WithMessage("*not_outcome_unknown*");
    }

    [Fact]
    public void ReclaimExpiredClaim_AfterExpiry_RotatesToken()
    {
        var attempt = Claim();
        var replacement = Guid.NewGuid();

        attempt.ReclaimExpiredClaim(replacement, Now.AddMinutes(20), Now.AddMinutes(10));

        attempt.LeaseToken.Should().Be(replacement);
        attempt.LastErrorCode.Should().Be("lease_expired");
    }

    [Fact]
    public void HasActiveLease_ReflectsExpiry()
    {
        var attempt = Claim();

        attempt.HasActiveLease(Now.AddMinutes(1)).Should().BeTrue();
        attempt.HasActiveLease(Now.AddMinutes(10)).Should().BeFalse();
    }
}
