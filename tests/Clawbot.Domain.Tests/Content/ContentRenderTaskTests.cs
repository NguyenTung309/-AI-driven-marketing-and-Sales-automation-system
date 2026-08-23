using Clawbot.Domain.Content;
using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

// Máy trạng thái tác vụ render nội dung: pending -> leased -> completed/failed; lease token + hạn + retry.
public sealed class ContentRenderTaskTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Item = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
    private const string TemplateSha = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    // Slots JSON chuẩn + hash khớp lấy từ canonicalizer thật.
    private static (string Json, string Hash) CanonicalSlots()
    {
        var slots = new[] { ContentVisualSlot.Create("title", ["Khai giảng"]) };
        return (
            ContentRenderSpecCanonicalizer.ToCanonicalSlotsJson(slots),
            ContentRenderSpecCanonicalizer.ComputeSlotsSha256(slots));
    }

    private static ContentRenderTask CreatePending(int sourceRevision = 1)
    {
        var (json, hash) = CanonicalSlots();
        return ContentRenderTask.CreatePending(
            Tenant, Item, sourceRevision,
            templateId: "promo-card", templateVersion: 1, templateHash: TemplateSha,
            preset: "1200x630", canonicalSlotsJson: json, slotsHash: hash,
            nextAttemptAt: Now, createdAt: Now);
    }

    [Fact]
    public void CreatePending_Valid_SetsPendingState()
    {
        var task = CreatePending();

        task.Status.Should().Be(ContentRenderTask.StatusPending);
        task.TenantId.Should().Be(Tenant);
        task.AttemptCount.Should().Be(0);
        task.TemplateId.Should().Be("promo-card");
    }

    [Fact]
    public void CreatePending_SlotsHashMismatch_Throws()
    {
        var (json, _) = CanonicalSlots();
        var act = () => ContentRenderTask.CreatePending(
            Tenant, Item, 1, "promo-card", 1, TemplateSha, "1200x630", json,
            slotsHash: new string('0', 64), nextAttemptAt: Now, createdAt: Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreatePending_InvalidSourceRevision_Throws()
    {
        var (json, hash) = CanonicalSlots();
        var act = () => ContentRenderTask.CreatePending(
            Tenant, Item, 0, "promo-card", 1, TemplateSha, "1200x630", json, hash, Now, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CreatePending_InvalidPreset_Throws()
    {
        var (json, hash) = CanonicalSlots();
        var act = () => ContentRenderTask.CreatePending(
            Tenant, Item, 1, "promo-card", 1, TemplateSha, "999x999", json, hash, Now, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Lease_FromPending_TransitionsToLeased()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();

        task.Lease(token, Now.AddMinutes(5), Now);

        task.Status.Should().Be(ContentRenderTask.StatusLeased);
        task.LeaseToken.Should().Be(token);
        task.AttemptCount.Should().Be(1);
        task.StartedAt.Should().Be(Now);
    }

    [Fact]
    public void Lease_NotDue_Throws()
    {
        var task = CreatePending();
        var act = () => task.Lease(Guid.NewGuid(), Now.AddMinutes(5), Now.AddMinutes(-1));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not_due*");
    }

    [Fact]
    public void Lease_ExpiryNotAfterNow_Throws()
    {
        var task = CreatePending();
        var act = () => task.Lease(Guid.NewGuid(), Now, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Complete_WithActiveLease_TransitionsToCompleted()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();
        task.Lease(token, Now.AddMinutes(5), Now);

        var output = Guid.NewGuid();
        task.Complete(token, output, completedRevision: 2, Now.AddMinutes(1));

        task.Status.Should().Be(ContentRenderTask.StatusCompleted);
        task.OutputAssetId.Should().Be(output);
        task.CompletedRevision.Should().Be(2);
        task.LeaseToken.Should().BeNull();
    }

    [Fact]
    public void Complete_WrongRevision_Throws()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();
        task.Lease(token, Now.AddMinutes(5), Now);

        var act = () => task.Complete(token, Guid.NewGuid(), completedRevision: 5, Now.AddMinutes(1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Complete_LeaseMismatch_Throws()
    {
        var task = CreatePending();
        task.Lease(Guid.NewGuid(), Now.AddMinutes(5), Now);

        var act = () => task.Complete(Guid.NewGuid(), Guid.NewGuid(), 2, Now.AddMinutes(1));
        act.Should().Throw<InvalidOperationException>().WithMessage("*lease_mismatch*");
    }

    [Fact]
    public void ReleaseForRetry_ReturnsToPending()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();
        task.Lease(token, Now.AddMinutes(5), Now);

        task.ReleaseForRetry(token, Now.AddMinutes(10), "transient_error", Now.AddMinutes(1));

        task.Status.Should().Be(ContentRenderTask.StatusPending);
        task.LeaseToken.Should().BeNull();
        task.LastErrorCode.Should().Be("transient_error");
    }

    [Fact]
    public void Fail_WithActiveLease_TransitionsToFailed()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();
        task.Lease(token, Now.AddMinutes(5), Now);

        task.Fail(token, "fatal_error", Now.AddMinutes(1));

        task.Status.Should().Be(ContentRenderTask.StatusFailed);
        task.LastErrorCode.Should().Be("fatal_error");
    }

    [Fact]
    public void TryClaimDelivery_FirstClaimTrue_SecondFalse()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();
        task.Lease(token, Now.AddMinutes(5), Now);

        task.TryClaimDelivery(token, Now.AddMinutes(1)).Should().BeTrue();
        task.TryClaimDelivery(token, Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void ReclaimExpiredLease_AfterExpiry_RotatesToken()
    {
        var task = CreatePending();
        var first = Guid.NewGuid();
        task.Lease(first, Now.AddMinutes(5), Now);

        var replacement = Guid.NewGuid();
        task.ReclaimExpiredLease(replacement, Now.AddMinutes(15), Now.AddMinutes(10));

        task.LeaseToken.Should().Be(replacement);
        task.AttemptCount.Should().Be(2);
        task.LastErrorCode.Should().Be("lease_expired");
    }

    [Fact]
    public void ReclaimExpiredLease_NotYetExpired_Throws()
    {
        var task = CreatePending();
        var first = Guid.NewGuid();
        task.Lease(first, Now.AddMinutes(5), Now);

        var act = () => task.ReclaimExpiredLease(Guid.NewGuid(), Now.AddMinutes(15), Now.AddMinutes(1));
        act.Should().Throw<InvalidOperationException>().WithMessage("*lease_not_expired*");
    }

    [Fact]
    public void CancelStale_FromPending_TransitionsToCanceled()
    {
        var task = CreatePending();

        task.CancelStale(Now.AddMinutes(1));

        task.Status.Should().Be(ContentRenderTask.StatusCanceledStale);
        task.LastErrorCode.Should().Be("stale_content_revision");
    }

    [Fact]
    public void CancelStale_WhenTerminal_Throws()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();
        task.Lease(token, Now.AddMinutes(5), Now);
        task.Fail(token, "x", Now.AddMinutes(1));

        var act = () => task.CancelStale(Now.AddMinutes(2));
        act.Should().Throw<InvalidOperationException>().WithMessage("*terminal*");
    }

    [Fact]
    public void FailExhausted_AttemptLimitNotReached_Throws()
    {
        var task = CreatePending();
        var token = Guid.NewGuid();
        task.Lease(token, Now.AddMinutes(5), Now); // AttemptCount = 1

        var act = () => task.FailExhausted(maxAttempts: 3, Now.AddMinutes(10));
        act.Should().Throw<InvalidOperationException>().WithMessage("*attempt_limit_not_reached*");
    }
}
