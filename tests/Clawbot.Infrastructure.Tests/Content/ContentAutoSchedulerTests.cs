using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content;
using Clawbot.SharedKernel.Content;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Content;

// Phase 3.1 RED → 3.2 GREEN: revision-bound schedule intent created in the approval UoW,
// golden time persisted once, concurrent winner load, user-cancel not auto-recreated,
// missing target leaves held intent at the original golden time.
public sealed class ContentAutoSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateIntentAsync_uses_golden_hour_and_current_revision()
    {
        using var fx = new TestAppDb();
        // Non-Facebook: no MetaAsset FK required for a pending intent with null target.
        var item = await SeedApprovedItemAsync(fx, platform: "tiktok");
        var golden = Substitute.For<IGoldenHourResolver>();
        var goldenAt = new DateTimeOffset(2026, 7, 21, 20, 0, 0, TimeSpan.FromHours(7));
        golden.ResolveNext("tiktok", Now).Returns(goldenAt);
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var schedule = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now);

        schedule.ContentItemId.Should().Be(item.Id);
        schedule.TenantId.Should().Be(fx.TenantId);
        schedule.ContentRevision.Should().Be(item.ContentRevision);
        schedule.ActiveRevisionSlot.Should().Be(item.ContentRevision);
        schedule.Platform.Should().Be("tiktok");
        schedule.ScheduledAt.Should().Be(goldenAt);
        schedule.Status.Should().Be(ContentSchedule.StatusPending);
        schedule.ApprovalMode.Should().Be(ContentItem.ApprovalModeAutomatic);
        schedule.PublishingPolicyVersionApplied.Should().Be(1);
        schedule.PublishTargetId.Should().BeNull();
        schedule.MetaAssetId.Should().BeNull();
        item.Status.Should().Be("scheduled");
        item.DesiredPublishAt.Should().Be(goldenAt);
        golden.Received(1).ResolveNext("tiktok", Now);

        await fx.Db.SaveChangesAsync();
        var saved = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        saved.Id.Should().Be(schedule.Id);
        saved.ScheduledAt.Should().Be(goldenAt);
        saved.ContentRevision.Should().Be(item.ContentRevision);
    }

    [Fact]
    public async Task CreateIntentAsync_uses_explicit_desired_publish_at_without_golden_hour()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "youtube");
        var golden = Substitute.For<IGoldenHourResolver>();
        var explicitAt = Now.AddHours(6);
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var schedule = await scheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            Now,
            desiredPublishAt: explicitAt);

        schedule.ScheduledAt.Should().Be(explicitAt);
        schedule.Status.Should().Be(ContentSchedule.StatusPending);
        item.DesiredPublishAt.Should().Be(explicitAt);
        golden.DidNotReceive().ResolveNext(Arg.Any<string>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task CreateIntentAsync_rejects_past_desired_publish_at()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "youtube");
        var scheduler = new ContentAutoScheduler(fx.Db, Substitute.For<IGoldenHourResolver>());

        var act = async () => await scheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            Now,
            desiredPublishAt: Now.AddMinutes(-1));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*content_schedule_in_past*");
    }

    [Fact]
    public async Task CreateIntentAsync_is_idempotent_for_active_revision_intent()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "tiktok");
        var golden = Substitute.For<IGoldenHourResolver>();
        var firstGolden = new DateTimeOffset(2026, 7, 21, 20, 0, 0, TimeSpan.FromHours(7));
        var laterGolden = firstGolden.AddDays(1);
        golden.ResolveNext("tiktok", Now).Returns(firstGolden, laterGolden);
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var first = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now);
        await fx.Db.SaveChangesAsync();

        // Simulate later call in a new scope/time without recreating golden selection.
        var second = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now.AddMinutes(5));

        second.Id.Should().Be(first.Id);
        second.ScheduledAt.Should().Be(firstGolden);
        golden.Received(1).ResolveNext(Arg.Any<string>(), Arg.Any<DateTimeOffset>());
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateIntentAsync_updates_active_held_facebook_intent_with_selected_target_and_time()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "facebook");
        var golden = Substitute.For<IGoldenHourResolver>();
        var originalAt = Now.AddHours(4);
        var rescheduledAt = Now.AddHours(8);
        golden.ResolveNext("facebook", Now).Returns(originalAt);
        var scheduler = new ContentAutoScheduler(fx.Db, golden);
        var existing = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now);
        await fx.Db.SaveChangesAsync();
        var selectedPage = Guid.NewGuid();

        var updated = await scheduler.CreateIntentAsync(
            item,
            selectedPage,
            Now.AddMinutes(10),
            desiredPublishAt: rescheduledAt);

        updated.Id.Should().Be(existing.Id);
        updated.Status.Should().Be(ContentSchedule.StatusPending);
        updated.ScheduledAt.Should().Be(rescheduledAt);
        updated.MetaAssetId.Should().Be(selectedPage);
        updated.PublishTargetId.Should().Be(selectedPage);
        updated.LastErrorCode.Should().BeNull();
        item.DesiredPublishAt.Should().Be(rescheduledAt);
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateIntentAsync_updates_active_zalo_intent_with_explicit_time()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "zalo");
        var golden = Substitute.For<IGoldenHourResolver>();
        golden.ResolveNext("zalo", Now).Returns(Now.AddHours(4));
        var scheduler = new ContentAutoScheduler(fx.Db, golden);
        var existing = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now);
        await fx.Db.SaveChangesAsync();
        var explicitAt = Now.AddHours(9);

        var updated = await scheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            Now.AddMinutes(10),
            desiredPublishAt: explicitAt);

        updated.Id.Should().Be(existing.Id);
        updated.ScheduledAt.Should().Be(explicitAt);
        updated.Status.Should().Be(ContentSchedule.StatusPending);
        item.DesiredPublishAt.Should().Be(explicitAt);
    }

    [Fact]
    public async Task CreateIntentAsync_preserves_instagram_target_snapshot_when_only_time_changes()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "instagram");
        var golden = Substitute.For<IGoldenHourResolver>();
        golden.ResolveNext("instagram", Now).Returns(Now.AddHours(4));
        var scheduler = new ContentAutoScheduler(fx.Db, golden);
        var existing = await scheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            Now,
            providerTargetId: "17841400000000000");
        await fx.Db.SaveChangesAsync();
        var rescheduledAt = Now.AddHours(8);

        var updated = await scheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            Now.AddMinutes(10),
            desiredPublishAt: rescheduledAt);

        updated.Id.Should().Be(existing.Id);
        updated.ScheduledAt.Should().Be(rescheduledAt);
        updated.MetaAssetId.Should().BeNull();
        updated.ProviderTargetId.Should().Be("17841400000000000");
        updated.LastErrorCode.Should().Be(ContentAutoScheduler.ErrorInstagramPublishingUnavailable);
    }

    [Fact]
    public async Task CreateIntentAsync_preserves_legacy_instagram_reselection_hold_on_time_only_change()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "instagram");
        var golden = Substitute.For<IGoldenHourResolver>();
        golden.ResolveNext("instagram", Now).Returns(Now.AddHours(4));
        var scheduler = new ContentAutoScheduler(fx.Db, golden);
        var existing = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now);
        existing.MarkHeld(ContentSchedule.ErrorInstagramTargetReselectionRequired, Now.AddMinutes(1));
        await fx.Db.SaveChangesAsync();
        var rescheduledAt = Now.AddHours(8);

        var updated = await scheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            Now.AddMinutes(10),
            desiredPublishAt: rescheduledAt);

        updated.Id.Should().Be(existing.Id);
        updated.ScheduledAt.Should().Be(rescheduledAt);
        updated.Status.Should().Be(ContentSchedule.StatusHeld);
        updated.ProviderTargetId.Should().BeNull();
        updated.LastErrorCode.Should().Be(ContentSchedule.ErrorInstagramTargetReselectionRequired);
    }

    [Fact]
    public async Task CreateIntentAsync_accepts_standalone_instagram_target_without_meta_asset()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "instagram");
        var golden = Substitute.For<IGoldenHourResolver>();
        golden.ResolveNext("instagram", Now).Returns(Now.AddHours(4));
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var schedule = await scheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            Now,
            providerTargetId: "17841400000000000");

        schedule.MetaAssetId.Should().BeNull();
        schedule.ProviderTargetId.Should().Be("17841400000000000");
        schedule.LastErrorCode.Should().Be(ContentAutoScheduler.ErrorInstagramPublishingUnavailable);
    }

    [Fact]
    public async Task CreateIntentAsync_does_not_recreate_user_canceled_intent()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "youtube");
        var golden = Substitute.For<IGoldenHourResolver>();
        golden.ResolveNext("youtube", Arg.Any<DateTimeOffset>())
            .Returns(new DateTimeOffset(2026, 7, 21, 18, 0, 0, TimeSpan.FromHours(7)));
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var created = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now);
        await fx.Db.SaveChangesAsync();
        created.Cancel(Now.AddMinutes(1), ContentSchedule.ErrorCanceledByUser);
        item.RevertToApproved(Now.AddMinutes(1));
        await fx.Db.SaveChangesAsync();

        var act = async () => await scheduler.CreateIntentAsync(item, publishTargetId: null, Now.AddMinutes(2));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*content_schedule_canceled_by_user*");
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var only = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        only.Status.Should().Be(ContentSchedule.StatusCanceled);
        only.LastErrorCode.Should().Be(ContentSchedule.ErrorCanceledByUser);
        only.ScheduledAt.Should().Be(created.ScheduledAt);
        golden.Received(1).ResolveNext(Arg.Any<string>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task CreateIntentAsync_holds_when_facebook_target_missing_without_moving_golden_time()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "facebook");
        var golden = Substitute.For<IGoldenHourResolver>();
        var goldenAt = new DateTimeOffset(2026, 7, 21, 20, 30, 0, TimeSpan.FromHours(7));
        golden.ResolveNext("facebook", Now).Returns(goldenAt);
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var schedule = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now);
        await fx.Db.SaveChangesAsync();

        schedule.Status.Should().Be(ContentSchedule.StatusHeld);
        schedule.LastErrorCode.Should().Be(ContentAutoScheduler.ErrorAutoScheduleTargetMissing);
        schedule.ScheduledAt.Should().Be(goldenAt);
        schedule.PublishTargetId.Should().BeNull();
        item.Status.Should().Be("scheduled");
        item.DesiredPublishAt.Should().Be(goldenAt);

        // Retry / second ensure keeps original golden time and does not re-resolve.
        var again = await scheduler.CreateIntentAsync(item, publishTargetId: null, Now.AddHours(1));
        again.Id.Should().Be(schedule.Id);
        again.ScheduledAt.Should().Be(goldenAt);
        golden.Received(1).ResolveNext("facebook", Now);
    }

    [Fact]
    public async Task CreateIntentAsync_holds_instagram_until_native_publishing_is_configured()
    {
        using var fx = new TestAppDb();
        var item = await SeedApprovedItemAsync(fx, platform: "instagram");
        var golden = Substitute.For<IGoldenHourResolver>();
        var goldenAt = Now.AddHours(4);
        golden.ResolveNext("instagram", Now).Returns(goldenAt);
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var pageAssetId = Guid.NewGuid();
        var schedule = await scheduler.CreateIntentAsync(
            item,
            publishTargetId: pageAssetId,
            Now,
            providerTargetId: "ig-user-123");

        schedule.Status.Should().Be(ContentSchedule.StatusHeld);
        schedule.LastErrorCode.Should().Be(ContentAutoScheduler.ErrorInstagramPublishingUnavailable);
        schedule.ScheduledAt.Should().Be(goldenAt);
        schedule.MetaAssetId.Should().Be(pageAssetId);
        schedule.ProviderTargetId.Should().Be("ig-user-123");
        item.Status.Should().Be("scheduled");
    }

    [Fact]
    public async Task CreateIntentAsync_rejects_item_that_is_not_schedulable()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Draft only", createdBy: null, Now);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var golden = Substitute.For<IGoldenHourResolver>();
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var act = async () => await scheduler.CreateIntentAsync(item, Guid.NewGuid(), Now);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*content_current_revision_not_schedulable*");
        golden.DidNotReceiveWithAnyArgs().ResolveNext(default!, default);
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateIntentAsync_rejects_legacy_approve_without_publishing_approval_fields()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "Body", createdBy: null, Now);
        item.BeginAgentReview(1, Now.AddMinutes(-20));
        item.RecordAgentReview(
            1,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            at: Now.AddMinutes(-10));
        // Legacy Approve sets status approved without ApprovedRevision/ApprovalMode.
        item.Approve(Guid.NewGuid(), Now.AddMinutes(-5));
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var golden = Substitute.For<IGoldenHourResolver>();
        var scheduler = new ContentAutoScheduler(fx.Db, golden);

        var act = async () => await scheduler.CreateIntentAsync(item, Guid.NewGuid(), Now);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*content_current_revision_not_schedulable*");
    }

    private static async Task<ContentItem> SeedApprovedItemAsync(TestAppDb fx, string platform)
    {
        var item = ContentItem.Create(fx.TenantId, platform, "Auto schedule body", createdBy: null, Now.AddHours(-2));
        item.BeginAgentReview(1, Now.AddMinutes(-90));
        item.RecordAgentReview(
            1,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            at: Now.AddMinutes(-80));
        item.ApproveAutomatically(
            1,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            at: Now.AddMinutes(-70));
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        return item;
    }
}
