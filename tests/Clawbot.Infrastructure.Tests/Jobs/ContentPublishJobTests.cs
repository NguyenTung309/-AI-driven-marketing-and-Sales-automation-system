using Clawbot.Domain.Content;
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
        item.MarkScheduled(Now.AddHours(-1));
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
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
    public async Task RunAsync_retries_on_first_failure_and_does_not_notify()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "youtube", "Video post", createdBy: null, Now.AddHours(-2));
        item.MarkScheduled(Now.AddHours(-1));
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, "youtube", Now.AddMinutes(-5), Now.AddHours(-1));
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
        item.MarkScheduled(Now.AddHours(-1));
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, "youtube", Now.AddMinutes(-5), Now.AddHours(-1));
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
    public async Task RunAsync_holds_unreviewed_item_when_tenant_requires_review()
    {
        // Review-gate P1 (G1): schedule stays pending, publisher never called, item untouched.
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        item.Approve(Guid.NewGuid(), Now.AddHours(-1)); // human approved only — no agent signoff
        item.MarkScheduled(Now.AddHours(-1));
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingPublisher(new PublishResult(true, "https://social.example/posts/1", null));
        var notifier = Substitute.For<IContentNotifier>();
        var job = BuildJob(fx, publisher, notifier, reviewRequired: true);

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().BeEmpty();
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("pending");
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("scheduled");
    }

    [Fact]
    public async Task RunAsync_publishes_reviewed_item_when_tenant_requires_review()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        item.ApproveByAgent(Guid.NewGuid(), Now.AddHours(-1)); // reviewer signoff present
        item.MarkScheduled(Now.AddHours(-1));
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
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
    public async Task RunAsync_skips_stale_schedule_when_item_no_longer_scheduled()
    {
        // Item reverted to approved after scheduling — pending schedule must not publish it.
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Post body", createdBy: null, Now.AddHours(-2));
        item.ApproveByAgent(Guid.NewGuid(), Now.AddHours(-1));
        item.MarkScheduled(Now.AddHours(-1));
        var schedule = ContentSchedule.Schedule(fx.TenantId, item.Id, "facebook", Now.AddMinutes(-5), Now.AddHours(-1));
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
    }

    private sealed class FakeReviewPolicy(bool required) : IContentReviewPolicyResolver
    {
        public Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(required);
    }

    private static ContentPublishJob BuildJob(
        TestAppDb fx,
        ISocialPublisher publisher,
        IContentNotifier notifier,
        bool reviewRequired = false)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ContentPublishJob(
            fx.Db,
            publisher,
            notifier,
            clock,
            NullLogger<ContentPublishJob>.Instance,
            new FakeReviewPolicy(reviewRequired));
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
