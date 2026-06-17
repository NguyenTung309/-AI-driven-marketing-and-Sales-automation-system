using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class RetentionPurgeJobTests
{
    [Fact]
    public async Task RunAsync_removes_notifications_older_than_90_days_only()
    {
        using var t = new TestAppDb();
        var now = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
        var expired = Notification.Create(t.TenantId, null, "system", "expired", now.AddDays(-91));
        var boundary = Notification.Create(t.TenantId, null, "system", "boundary", now.AddDays(-90));
        var fresh = Notification.Create(t.TenantId, null, "system", "fresh", now.AddDays(-5));
        t.Db.Notifications.AddRange(expired, boundary, fresh);
        await t.Db.SaveChangesAsync();

        var sut = new RetentionPurgeJob(t.Db, NullLogger<RetentionPurgeJob>.Instance, new FixedTimeProvider(now));
        await sut.RunAsync();

        var remainingTitles = await t.Db.Notifications
            .IgnoreQueryFilters()
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Title)
            .ToListAsync();
        remainingTitles.Should().Equal("boundary", "fresh");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
