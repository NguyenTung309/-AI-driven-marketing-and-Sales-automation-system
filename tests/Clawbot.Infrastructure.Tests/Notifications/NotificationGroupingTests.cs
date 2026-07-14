using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Notifications;
using Clawbot.SharedKernel.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Notifications;

// Feed kiểu Facebook: việc máy móc lặp lại phải gom về 1 dòng, không đẻ 5 dòng làm user tắt chuông.
public sealed class NotificationGroupingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private static NotificationRequest Request(Guid tenantId, string? groupKey, string body = "b") =>
        new(tenantId, null, "ads_daypart", "AI đã chỉnh quảng cáo", "info", body, "/ads", groupKey);

    [Fact]
    public async Task Upsert_merges_same_group_into_one_row()
    {
        using var fx = new TestAppDb();

        Notification? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = await NotificationGrouping.UpsertAsync(
                fx.Db, Request(fx.TenantId, "ads.daypart:20260713", $"lần {i}"), Now.AddMinutes(i), default);
        }

        // Giá trị TRẢ VỀ mới là thứ publisher đẩy realtime xuống chuông — phải mang số đếm mới,
        // không phải bản đang tracking (ExecuteUpdate ghi thẳng DB, không đụng ChangeTracker).
        last!.OccurrenceCount.Should().Be(5);

        var rows = await fx.Db.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].OccurrenceCount.Should().Be(5);
        rows[0].Body.Should().Be("lần 4", "body lấy theo sự kiện mới nhất");
        rows[0].LastOccurredAt.Should().Be(Now.AddMinutes(4));
    }

    [Fact]
    public async Task Upsert_creates_new_row_when_group_already_read()
    {
        using var fx = new TestAppDb();
        var first = await NotificationGrouping.UpsertAsync(
            fx.Db, Request(fx.TenantId, "ads.daypart:20260713"), Now, default);

        first.MarkRead(Now.AddMinutes(1));
        await fx.Db.SaveChangesAsync();

        await NotificationGrouping.UpsertAsync(
            fx.Db, Request(fx.TenantId, "ads.daypart:20260713"), Now.AddMinutes(2), default);

        var rows = await fx.Db.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2, "nhóm đã đọc rồi thì sự kiện mới phải nổi lên lại");
    }

    [Fact]
    public async Task Upsert_creates_new_row_when_outside_window()
    {
        using var fx = new TestAppDb();
        await NotificationGrouping.UpsertAsync(fx.Db, Request(fx.TenantId, "g1"), Now, default);
        await NotificationGrouping.UpsertAsync(
            fx.Db, Request(fx.TenantId, "g1"), Now.Add(NotificationGrouping.Window).AddMinutes(1), default);

        var rows = await fx.Db.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Upsert_without_group_key_never_merges()
    {
        using var fx = new TestAppDb();
        await NotificationGrouping.UpsertAsync(fx.Db, Request(fx.TenantId, null), Now, default);
        await NotificationGrouping.UpsertAsync(fx.Db, Request(fx.TenantId, null), Now.AddMinutes(1), default);

        var rows = await fx.Db.Notifications.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(n => n.OccurrenceCount == 1);
    }
}
