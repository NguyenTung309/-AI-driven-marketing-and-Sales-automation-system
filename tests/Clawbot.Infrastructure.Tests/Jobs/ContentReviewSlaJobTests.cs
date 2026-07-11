using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

// Review-gate P4: SLA job nhắc bài chờ review sát/quá giờ đăng — mỗi tier đúng 1 lần, không auto-approve.
public sealed class ContentReviewSlaJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_tier1_alerts_creator_once_before_deadline()
    {
        using var fx = new TestAppDb();
        var creator = Guid.NewGuid();
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: creator, Now.AddHours(-1));
        item.Approve(creator, Now.AddMinutes(-30)); // human approved, chưa có chữ ký agent
        item.SetDesiredPublishAt(Now.AddMinutes(30), Now.AddMinutes(-30)); // trong lead-time 60'
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<INotificationPublisher>();
        var job = BuildJob(fx, publisher);

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None); // pass 2: không re-alert

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(n => n.Type == "content_review_pending" && n.UserId == creator && n.TenantId == fx.TenantId),
            Arg.Any<CancellationToken>());
        fx.Db.ChangeTracker.Clear();
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).LastReviewAlertAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_tier2_escalates_scheduled_unreviewed_item_past_deadline()
    {
        // Critique #4: item đã 'scheduled' nhưng chưa ký — publish job skip nó mỗi pass; SLA job PHẢI thấy.
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now.AddHours(-3));
        item.Approve(Guid.NewGuid(), Now.AddHours(-2));
        item.MarkScheduled(Now.AddHours(-2));
        item.SetDesiredPublishAt(Now.AddMinutes(-10), Now.AddHours(-2)); // đã quá hạn
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<INotificationPublisher>();
        var lead1 = Guid.NewGuid();
        var lead2 = Guid.NewGuid();
        var job = BuildJob(fx, publisher, recipients: [lead1, lead2]);

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None); // pass 2: không re-escalate

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(n => n.Type == "content_review_overdue" && n.UserId == lead1),
            Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(n => n.Type == "content_review_overdue" && n.UserId == lead2),
            Arg.Any<CancellationToken>());
        // QĐ4: không auto-approve — item giữ nguyên, chỉ notify.
        fx.Db.ChangeTracker.Clear();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.ApprovedByAgentId.Should().BeNull();
        saved.Status.Should().Be("scheduled");
    }

    [Fact]
    public async Task RunAsync_tier2_falls_back_to_tenant_broadcast_when_no_recipients()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "zalo", "body", createdBy: null, Now.AddHours(-3));
        item.Approve(Guid.NewGuid(), Now.AddHours(-2));
        item.SetDesiredPublishAt(Now.AddMinutes(-5), Now.AddHours(-2));
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<INotificationPublisher>();
        var job = BuildJob(fx, publisher, recipients: []);

        await job.RunAsync(CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(n => n.Type == "content_review_overdue" && n.UserId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_skips_signed_items_and_flag_off_tenants()
    {
        using var fx = new TestAppDb();
        var signed = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now.AddHours(-2));
        signed.ApproveByAgent(Guid.NewGuid(), Now.AddHours(-1)); // đã có chữ ký -> khỏi nhắc
        signed.SetDesiredPublishAt(Now.AddMinutes(-5), Now.AddHours(-1));
        fx.Db.ContentItems.Add(signed);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<INotificationPublisher>();
        var job = BuildJob(fx, publisher);

        await job.RunAsync(CancellationToken.None);
        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);

        // Flag off: item unsigned quá hạn nhưng tenant không bật gate -> im lặng.
        var unsigned = ContentItem.Create(fx.TenantId, "facebook", "body2", createdBy: null, Now.AddHours(-2));
        unsigned.Approve(Guid.NewGuid(), Now.AddHours(-1));
        unsigned.SetDesiredPublishAt(Now.AddMinutes(-5), Now.AddHours(-1));
        fx.Db.ContentItems.Add(unsigned);
        await fx.Db.SaveChangesAsync();
        var offJob = BuildJob(fx, publisher, reviewRequired: false);

        await offJob.RunAsync(CancellationToken.None);
        await publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    private sealed class FakeReviewPolicy(bool required) : IContentReviewPolicyResolver
    {
        public Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(required);
    }

    private sealed class FakeRecipients(IReadOnlyList<Guid> ids) : IContentReviewEscalationRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(ids);
    }

    private static ContentReviewSlaJob BuildJob(
        TestAppDb fx,
        INotificationPublisher publisher,
        IReadOnlyList<Guid>? recipients = null,
        bool reviewRequired = true)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ContentReviewSlaJob(
            fx.Db,
            publisher,
            new FakeReviewPolicy(reviewRequired),
            new FakeRecipients(recipients ?? []),
            clock,
            NullLogger<ContentReviewSlaJob>.Instance);
    }
}
