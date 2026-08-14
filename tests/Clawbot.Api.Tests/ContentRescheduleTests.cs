using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

/// <summary>
/// Đổi lịch một bài đã lên lịch: người dùng bấm "Đổi lịch (tuỳ chọn)" rồi chọn giờ vàng
/// hoặc thời điểm riêng. Cả hai đường đều phải đi qua ContentAutoScheduler mà không vỡ
/// concurrency token (409) hay bị chặn bởi guard trạng thái.
/// </summary>
public sealed class ContentRescheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateIntentAsync_ExplicitTime_ReschedulesAlreadyScheduledItem()
    {
        // Arrange
        await using var fixture = await RescheduleFixture.CreateAsync();
        var (item, schedule) = await fixture.SeedScheduledItemAsync();
        var newTime = Now.AddDays(1);

        // Act
        var result = await fixture.Scheduler.CreateIntentAsync(
            item,
            publishTargetId: fixture.PageId,
            at: Now,
            desiredPublishAt: newTime);
        await fixture.Db.SaveChangesAsync();

        // Assert
        result.Id.Should().Be(schedule.Id);
        result.ScheduledAt.Should().Be(newTime);
        result.Status.Should().Be(ContentSchedule.StatusPending);
        result.LastErrorCode.Should().BeNull();
        item.DesiredPublishAt.Should().Be(newTime);
    }

    [Fact]
    public async Task CreateIntentAsync_GoldenHour_ReschedulesAlreadyScheduledItem()
    {
        // Arrange
        await using var fixture = await RescheduleFixture.CreateAsync();
        var (item, schedule) = await fixture.SeedScheduledItemAsync();

        // Act — giờ vàng: không truyền desiredPublishAt, chỉ đổi target.
        var result = await fixture.Scheduler.CreateIntentAsync(
            item,
            publishTargetId: fixture.PageId,
            at: Now);
        await fixture.Db.SaveChangesAsync();

        // Assert
        result.Id.Should().Be(schedule.Id);
        result.ScheduledAt.Should().Be(RescheduleFixture.GoldenSlot);
        result.Status.Should().Be(ContentSchedule.StatusPending);
    }

    [Fact]
    public async Task CreateIntentAsync_HeldTargetMissing_RecoversWhenPageResolved()
    {
        // Arrange — đúng ca người dùng gặp: schedule bị giữ vì thiếu Facebook Page.
        await using var fixture = await RescheduleFixture.CreateAsync();
        var (item, schedule) = await fixture.SeedScheduledItemAsync(withPublishTarget: false);
        schedule.Status.Should().Be(ContentSchedule.StatusHeld);
        schedule.LastErrorCode.Should().Be(ContentAutoScheduler.ErrorAutoScheduleTargetMissing);

        // Act
        var result = await fixture.Scheduler.CreateIntentAsync(
            item,
            publishTargetId: fixture.PageId,
            at: Now,
            desiredPublishAt: Now.AddHours(6));
        await fixture.Db.SaveChangesAsync();

        // Assert
        result.Status.Should().Be(ContentSchedule.StatusPending);
        result.LastErrorCode.Should().BeNull();
        result.MetaAssetId.Should().Be(fixture.PageId);
    }

    private sealed class RescheduleFixture(
        SqliteConnection connection,
        AppDbContext db,
        Guid tenantId) : IAsyncDisposable
    {
        public static readonly DateTimeOffset GoldenSlot = Now.AddHours(9);

        public AppDbContext Db { get; } = db;
        public Guid TenantId { get; } = tenantId;
        public Guid PageId { get; } = Guid.NewGuid();
        public ContentAutoScheduler Scheduler { get; } = new(db, new FixedGoldenHour());

        public static async Task<RescheduleFixture> CreateAsync()
        {
            var tenantId = Guid.NewGuid();
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, new FixedTenantAccessor(tenantId));
            await db.Database.EnsureCreatedAsync();
            return new RescheduleFixture(connection, db, tenantId);
        }

        /// <summary>Bài đã qua agent review + duyệt phát hành và đã có schedule đang hoạt động.</summary>
        public async Task<(ContentItem Item, ContentSchedule Schedule)> SeedScheduledItemAsync(
            bool withPublishTarget = true)
        {
            var item = ContentItem.Create(
                TenantId,
                "facebook",
                "Bài test",
                createdBy: Guid.NewGuid(),
                createdAt: Now.AddHours(-2));
            item.BeginAgentReview(item.ContentRevision, Now.AddMinutes(-90));
            item.RecordAgentReview(
                item.ContentRevision,
                reviewStatus: ContentItem.ReviewStatusPassed,
                imageStatus: ContentItem.ImageReviewStatusNotApplicable,
                reviewedImageCount: 0,
                reviewerAgentId: Guid.NewGuid(),
                reason: null,
                at: Now.AddHours(-1));
            item.ApproveForPublishing(
                item.ContentRevision,
                userId: Guid.NewGuid(),
                appliedPolicy: ContentItem.PublishingPolicyAutomatic,
                appliedPolicyVersion: 1,
                overrideReason: null,
                at: Now.AddMinutes(-30));
            Db.ContentItems.Add(item);
            await Db.SaveChangesAsync();

            var schedule = await Scheduler.CreateIntentAsync(
                item,
                publishTargetId: withPublishTarget ? PageId : null,
                at: Now.AddMinutes(-20));
            await Db.SaveChangesAsync();

            item.Status.Should().Be("scheduled");
            return (item, schedule);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedGoldenHour : IGoldenHourResolver
    {
        public DateTimeOffset ResolveNext(string platform, DateTimeOffset from) =>
            RescheduleFixture.GoldenSlot;
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
