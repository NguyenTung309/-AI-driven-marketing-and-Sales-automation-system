using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class ContentPublishJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_publishes_due_pending_schedule_and_marks_item_published()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var notifier = Substitute.For<IContentNotifier>();
        var job = BuildJob(fx, publisher, notifier);

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().ContainSingle().Which.Should().Be(new PublishRequest(
            fx.TenantId,
            item.Id,
            "facebook",
            "Post body",
            "[]",
            schedule.ScheduledAt));
        var savedSchedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        savedSchedule.Status.Should().Be("posted");
        savedSchedule.PostedAt.Should().Be(Now);
        savedSchedule.PostUrl.Should().Be("https://social.example/posts/1");
        var savedItem = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        savedItem.Status.Should().Be("published");
        savedItem.UpdatedAt.Should().Be(Now);
        await notifier.DidNotReceiveWithAnyArgs().NotifyPublishFailedAsync(default, default!, default);
    }

    [Fact]
    public async Task RunAsync_persists_provider_external_id_when_no_public_post_url_exists()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "zalo", "Article body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "zalo", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, null, null, "zalo-article-123"));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        var attempt = await fx.Db.ContentPublishAttempts.IgnoreQueryFilters().SingleAsync();
        attempt.ExternalPostId.Should().Be("zalo-article-123");
        var savedSchedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        savedSchedule.Status.Should().Be(ContentSchedule.StatusPosted);
        savedSchedule.PostUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_retries_on_first_failure_and_does_not_notify()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "youtube", "Video post", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "youtube", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(false, null, "publisher down"));
        var notifier = Substitute.For<IContentNotifier>();
        var job = BuildJob(fx, publisher, notifier);

        await job.RunAsync(CancellationToken.None);

        var savedSchedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        savedSchedule.Status.Should().Be("pending");
        savedSchedule.RetryCount.Should().Be(1);
        savedSchedule.LastError.Should().Be(ContentSchedule.ErrorPublisherFailure);
        savedSchedule.UpdatedAt.Should().Be(Now);
        var savedItem = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        savedItem.Status.Should().Be("scheduled");
        await notifier.DidNotReceiveWithAnyArgs().NotifyPublishFailedAsync(default, default!, default);
    }

    [Fact]
    public async Task RunAsync_marks_failed_after_max_retries_and_notifies()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "youtube", "Video post", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "youtube", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(false, null, "publisher down"));
        var notifier = Substitute.For<IContentNotifier>();
        var job = BuildJob(fx, publisher, notifier);

        for (var i = 0; i < ContentSchedule.MaxRetries; i++)
            await job.RunAsync(CancellationToken.None);

        var savedSchedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        savedSchedule.Status.Should().Be("failed");
        savedSchedule.RetryCount.Should().Be(ContentSchedule.MaxRetries);
        await notifier.Received(1).NotifyPublishFailedAsync(
            fx.TenantId,
            Arg.Is<ContentPublishFailedEvent>(e =>
                e.TenantId == fx.TenantId
                && e.ContentItemId == item.Id
                && e.ScheduleId == schedule.Id
                && e.Platform == "youtube"
                && e.Reason == "publisher down"
                && e.OccurredAt == Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_uses_revision_review_instead_of_legacy_agent_signoff_flag()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>(), reviewRequired: true);

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().ContainSingle();
        var posted = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        posted.Status.Should().Be(ContentSchedule.StatusPosted);
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("published");
    }

    [Fact]
    public async Task RunAsync_publishes_reviewed_item_when_tenant_requires_review()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        item.AttachAgentSignoff(Guid.NewGuid(), Now.AddMinutes(-50));
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var notifier = Substitute.For<IContentNotifier>();
        var job = BuildJob(fx, publisher, notifier, reviewRequired: true);

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().ContainSingle();
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("published");
    }

    [Fact]
    public async Task RunAsync_cancels_stale_schedule_when_item_no_longer_scheduled()
    {
        // Item reverted to approved after scheduling — cancel schedule with reason (free unique pending index).
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        item.ApproveByAgent(Guid.NewGuid(), Now.AddHours(-1));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        item.RevertToApproved(Now.AddMinutes(-30));
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var notifier = Substitute.For<IContentNotifier>();
        var job = BuildJob(fx, publisher, notifier);

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().BeEmpty();
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("approved");
        var saved = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be(ContentSchedule.StatusCanceled);
        saved.LastError.Should().Be(ContentSchedule.ErrorStaleItemPrefix + "approved");
    }

    [Fact]
    public async Task RunAsync_holds_schedule_with_mismatched_approval_context()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        schedule.SetApprovalContext(ContentItem.ApprovalModeHuman, 99, publishTargetId: null);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().BeEmpty();
        var saved = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be(ContentSchedule.StatusHeld);
        saved.LastErrorCode.Should().Be("approval_context_mismatch");
    }

    [Fact]
    public async Task RunAsync_holds_instagram_before_claiming_or_calling_publisher()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "instagram", "Image post", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            "instagram",
            Now.AddMinutes(-5),
            Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://instagram.example/posts/1", null));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().BeEmpty();
        var saved = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be(ContentSchedule.StatusHeld);
        saved.LastErrorCode.Should().Be(ContentAutoScheduler.ErrorInstagramPublishingUnavailable);
        (await fx.Db.ContentPublishAttempts.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PublishOneAsync_publishes_failed_schedule_after_reset()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        schedule.MarkFailed(Now.AddMinutes(-1), "publisher_down");
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        var (ok, error) = await job.PublishOneAsync(schedule.Id, CancellationToken.None);

        ok.Should().BeTrue();
        error.Should().BeNull();
        publisher.Requests.Should().ContainSingle();
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(ContentSchedule.StatusPosted);
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("published");
    }

    [Fact]
    public async Task RunAsync_first_failure_persists_last_error()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "youtube", "Video post", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "youtube", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(false, null, "publisher down"));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        var saved = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be(ContentSchedule.StatusPending);
        saved.LastError.Should().Be(ContentSchedule.ErrorPublisherFailure);
        saved.RetryCount.Should().Be(1);
    }

    [Theory]
    [InlineData("publisher_timeout")]
    [InlineData("facebook_timeout")]
    [InlineData("instagram_timeout")]
    [InlineData("instagram_unavailable")]
    [InlineData("instagram_error")]
    [InlineData("zalo_timeout")]
    public async Task RunAsync_uncertain_result_after_claim_keeps_item_locked_for_reconciliation(string timeoutCode)
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            "facebook",
            Now.AddMinutes(-5),
            Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(false, null, timeoutCode));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        var savedSchedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        savedSchedule.Status.Should().Be(ContentSchedule.StatusOutcomeUnknown);
        var attempt = await fx.Db.ContentPublishAttempts.IgnoreQueryFilters().SingleAsync();
        attempt.Status.Should().Be(ContentPublishAttempt.StatusOutcomeUnknown);
        attempt.ScheduleId.Should().Be(schedule.Id);
        var savedItem = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        savedItem.ActivePublishAttemptId.Should().Be(attempt.Id);
        savedItem.Invoking(x => x.ReviseBody("Changed", Now.AddMinutes(1)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_claim_persists_publish_attempt_snapshot_before_provider_call()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            "facebook",
            Now.AddMinutes(-5),
            Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        var attempt = await fx.Db.ContentPublishAttempts.IgnoreQueryFilters().SingleAsync();
        attempt.BodySnapshot.Should().Be("Post body");
        attempt.ContentRevision.Should().Be(item.ContentRevision);
        attempt.IdempotencyKey.Should().Contain(schedule.Id.ToString("N"));
        attempt.Status.Should().Be(ContentPublishAttempt.StatusSucceeded);
        publisher.Requests.Should().ContainSingle().Which.Body.Should().Be(attempt.BodySnapshot);
    }

    [Fact]
    public async Task RunAsync_does_not_publish_schedule_without_approval_context()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            "facebook",
            Now.AddMinutes(-5),
            Now.AddHours(-1));
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().BeEmpty();
        var saved = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be(ContentSchedule.StatusHeld);
        saved.LastErrorCode.Should().Be("approval_context_missing");
    }

    [Fact]
    public async Task RunAsync_does_not_publish_stale_schedule_revision()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body v1", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var staleSchedule = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            "facebook",
            Now.AddMinutes(-5),
            Now.AddHours(-1));
        ApplyApprovalContext(staleSchedule, item);
        item.ReviseBody("Post body v2", Now.AddMinutes(-30));
        PrepareForScheduling(item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(staleSchedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var job = BuildJob(fx, publisher, Substitute.For<IContentNotifier>());

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().BeEmpty();
        var saved = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be(ContentSchedule.StatusCanceled);
        saved.LastErrorCode.Should().Be("stale_content_revision");
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("scheduled");
    }

    private static void PrepareForScheduling(ContentItem item)
    {
        var revision = item.ContentRevision;
        item.BeginAgentReview(revision, Now.AddMinutes(-110));
        item.RecordAgentReview(
            revision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            at: Now.AddMinutes(-100));
        item.ApproveAutomatically(
            revision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            at: Now.AddMinutes(-90));
        item.MarkScheduled(Now.AddHours(-1));
    }

    private static void ApplyApprovalContext(ContentSchedule schedule, ContentItem item) =>
        schedule.SetApprovalContext(
            item.ApprovalMode!,
            item.PublishingPolicyVersionApplied!.Value,
            schedule.MetaAssetId);

    private static ContentPublishJob BuildJob(
        TestAppDb fx,
        ISocialPublisher publisher,
        IContentNotifier notifier,
        bool reviewRequired = false)
    {
        _ = reviewRequired;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var runtimeGate = Substitute.For<IContentWorkflowRuntimeGate>();
        runtimeGate.IsPublicationPausedAsync(Arg.Any<CancellationToken>()).Returns(false);
        runtimeGate.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContentWorkflowRuntimeGateSnapshot(
            PublicationPaused: false,
            MinimumWriterVersion: 0,
            UpdatedAt: Now,
            UpdatedBy: null,
            Notes: "test"));
        return new ContentPublishJob(
            fx.Db,
            publisher,
            notifier,
            runtimeGate,
            clock,
            NullLogger<ContentPublishJob>.Instance);
    }

    [Fact]
    public async Task RunAsync_skips_provider_when_runtime_gate_pauses_publication()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        PrepareForScheduling(item);
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, item.ContentRevision, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        ApplyApprovalContext(schedule, item);
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();

        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var notifier = Substitute.For<IContentNotifier>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var runtimeGate = Substitute.For<IContentWorkflowRuntimeGate>();
        runtimeGate.IsPublicationPausedAsync(Arg.Any<CancellationToken>()).Returns(true);
        var job = new ContentPublishJob(
            fx.Db,
            publisher,
            notifier,
            runtimeGate,
            clock,
            NullLogger<ContentPublishJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().BeEmpty();
        var savedSchedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        savedSchedule.Status.Should().Be(ContentSchedule.StatusPending);
    }

    private sealed class RecordingPublisher(PublishResult result) : ISocialPublisher
    {
        public List<PublishRequest> Requests { get; } = [];

        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct = default)
        {
            _ = ct;
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }
}
