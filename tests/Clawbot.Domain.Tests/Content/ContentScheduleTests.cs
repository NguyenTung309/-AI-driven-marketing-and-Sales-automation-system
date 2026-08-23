using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

// Máy trạng thái lịch đăng: pending -> publishing -> posted/failed/outcome_unknown; hold, cancel, retry, reschedule.
public sealed class ContentScheduleTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Item = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    private static ContentSchedule NewSchedule(string platform = "facebook") =>
        ContentSchedule.Schedule(Tenant, Item, contentRevision: 1, platform, Now.AddHours(1), Now);

    [Fact]
    public void Schedule_Valid_SetsPendingState()
    {
        var schedule = NewSchedule();

        schedule.Status.Should().Be(ContentSchedule.StatusPending);
        schedule.ContentRevision.Should().Be(1);
        schedule.ActiveRevisionSlot.Should().Be(1);
    }

    [Fact]
    public void Schedule_InvalidRevision_Throws()
    {
        var act = () => ContentSchedule.Schedule(Tenant, Item, 0, "facebook", Now, Now);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetApprovalContext_OnPending_Sets()
    {
        var schedule = NewSchedule();
        var target = Guid.NewGuid();

        schedule.SetApprovalContext(ContentItem.ApprovalModeAutomatic, 5, target);

        schedule.ApprovalMode.Should().Be("automatic");
        schedule.PublishingPolicyVersionApplied.Should().Be(5);
        schedule.PublishTargetId.Should().Be(target);
    }

    [Fact]
    public void SetApprovalContext_Twice_Throws()
    {
        var schedule = NewSchedule();
        schedule.SetApprovalContext(ContentItem.ApprovalModeHuman, 1, null);

        var act = () => schedule.SetApprovalContext(ContentItem.ApprovalModeHuman, 2, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*already_set*");
    }

    [Fact]
    public void SetApprovalContext_InvalidMode_Throws()
    {
        var schedule = NewSchedule();
        var act = () => schedule.SetApprovalContext("weird", 1, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkPublishing_ThenPosted_HappyPath()
    {
        var schedule = NewSchedule();

        schedule.MarkPublishing(Now.AddHours(1));
        schedule.Status.Should().Be(ContentSchedule.StatusPublishing);

        schedule.MarkPosted("https://fb.com/post/1", "ext_123", Now.AddHours(1).AddMinutes(1));
        schedule.Status.Should().Be(ContentSchedule.StatusPosted);
        schedule.PostUrl.Should().Be("https://fb.com/post/1");
        schedule.ExternalPostId.Should().Be("ext_123");
        schedule.ActiveRevisionSlot.Should().BeNull();
    }

    [Fact]
    public void MarkPosted_WhenNotPublishing_Throws()
    {
        var schedule = NewSchedule();
        var act = () => schedule.MarkPosted("url", null, Now);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not_publishing*");
    }

    [Fact]
    public void MarkPosted_InvalidExternalId_Throws()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);
        var act = () => schedule.MarkPosted("url", "bad id with spaces!", Now);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RecordRetry_UnderLimit_ReturnsPendingTrue()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);

        schedule.RecordRetry(Now, "transient").Should().BeTrue();
        schedule.Status.Should().Be(ContentSchedule.StatusPending);
        schedule.RetryCount.Should().Be(1);
    }

    [Fact]
    public void RecordRetry_AtLimit_FailsAndReturnsFalse()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);

        schedule.RecordRetry(Now).Should().BeTrue();  // 1
        schedule.RecordRetry(Now).Should().BeTrue();  // 2
        schedule.RecordRetry(Now).Should().BeFalse(); // 3 -> failed

        schedule.Status.Should().Be(ContentSchedule.StatusFailed);
        schedule.RetryCount.Should().Be(ContentSchedule.MaxRetries);
    }

    [Fact]
    public void MarkHeld_FromPending_TransitionsToHeld()
    {
        var schedule = NewSchedule();

        schedule.MarkHeld("needs_review", Now, Now.AddHours(2));

        schedule.Status.Should().Be(ContentSchedule.StatusHeld);
        schedule.LastErrorCode.Should().Be("needs_review");
        schedule.NextAttemptAt.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void MarkHeld_WeirdReason_FallsBackToPublisherError()
    {
        var schedule = NewSchedule();
        schedule.MarkHeld("has spaces & symbols", Now);

        // Reason không hợp lệ -> chuẩn hoá về publisher_error.
        schedule.LastErrorCode.Should().Be(ContentSchedule.ErrorPublisherFailure);
    }

    [Fact]
    public void Cancel_FromPending_TransitionsToCanceled()
    {
        var schedule = NewSchedule();

        schedule.Cancel(Now);

        schedule.Status.Should().Be(ContentSchedule.StatusCanceled);
        schedule.LastErrorCode.Should().Be(ContentSchedule.ErrorCanceledByUser);
    }

    [Fact]
    public void Cancel_WhenPublishing_Throws()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);
        var act = () => schedule.Cancel(Now);
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot_be_canceled*");
    }

    [Fact]
    public void TryResetForRetry_FromFailed_ResetsToPending()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);
        schedule.MarkFailed(Now, "publisher_error");

        schedule.TryResetForRetry(Now).Should().BeTrue();
        schedule.Status.Should().Be(ContentSchedule.StatusPending);
        schedule.RetryCount.Should().Be(0);
        schedule.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void TryResetForRetry_WhenPosted_ReturnsFalse()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);
        schedule.MarkPosted("url", null, Now);

        schedule.TryResetForRetry(Now).Should().BeFalse();
    }

    [Fact]
    public void Reschedule_FromPending_UpdatesTime()
    {
        var schedule = NewSchedule();
        var newTime = Now.AddDays(1);

        schedule.Reschedule(newTime, Guid.NewGuid(), "page-123", Now.AddMinutes(5));

        schedule.ScheduledAt.Should().Be(newTime);
        schedule.Status.Should().Be(ContentSchedule.StatusPending);
        schedule.ProviderTargetId.Should().Be("page-123");
    }

    [Fact]
    public void MarkOutcomeUnknown_ThenReconciledPosted()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);
        schedule.MarkOutcomeUnknown(Now, "timeout");

        schedule.Status.Should().Be(ContentSchedule.StatusOutcomeUnknown);

        schedule.MarkReconciledPosted("https://fb/1", "ext_9", Now.AddMinutes(1));
        schedule.Status.Should().Be(ContentSchedule.StatusPosted);
        schedule.ExternalPostId.Should().Be("ext_9");
    }

    [Fact]
    public void MarkReconciledFailed_FromOutcomeUnknown()
    {
        var schedule = NewSchedule();
        schedule.MarkPublishing(Now);
        schedule.MarkOutcomeUnknown(Now);

        schedule.MarkReconciledFailed(Now.AddMinutes(1), "confirmed_absent");
        schedule.Status.Should().Be(ContentSchedule.StatusFailed);
    }

    [Fact]
    public void RequiresInstagramTargetReselection_WhenIgWithoutTargetAndErrorSet()
    {
        var schedule = ContentSchedule.Schedule(Tenant, Item, 1, "instagram", Now, Now);
        schedule.MarkHeld(ContentSchedule.ErrorInstagramTargetReselectionRequired, Now);

        schedule.RequiresInstagramTargetReselection().Should().BeTrue();
        // Không reset được khi cần chọn lại target IG.
        schedule.TryResetForRetry(Now).Should().BeFalse();
    }

    [Fact]
    public void SetEngagement_UpdatesCountsAndTimestamp()
    {
        var schedule = NewSchedule();

        schedule.SetEngagement(10, 3, Now);

        schedule.LikeCount.Should().Be(10);
        schedule.CommentCount.Should().Be(3);
        schedule.EngagementSyncedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData("valid_id-123", true)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidExternalPostId_ValidatesFormat(string? id, bool expected)
    {
        ContentSchedule.IsValidExternalPostId(id).Should().Be(expected);
    }
}
