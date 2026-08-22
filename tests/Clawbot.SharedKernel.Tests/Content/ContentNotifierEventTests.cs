using Clawbot.SharedKernel.Content;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content;

public sealed class ContentNotifierEventTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ContentTrendScanEvent_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();

        var evt = new ContentTrendScanEvent(tenantId, 7, OccurredAt);

        evt.TenantId.Should().Be(tenantId);
        evt.TrendCount.Should().Be(7);
        evt.OccurredAt.Should().Be(OccurredAt);
    }

    [Fact]
    public void ContentPublishFailedEvent_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();
        var contentItemId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        var evt = new ContentPublishFailedEvent(
            tenantId, contentItemId, scheduleId, "facebook", "token_expired", OccurredAt);

        evt.TenantId.Should().Be(tenantId);
        evt.ContentItemId.Should().Be(contentItemId);
        evt.ScheduleId.Should().Be(scheduleId);
        evt.Platform.Should().Be("facebook");
        evt.Reason.Should().Be("token_expired");
        evt.OccurredAt.Should().Be(OccurredAt);
    }

    [Fact]
    public void AnalyticsAlertEvent_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();

        var evt = new AnalyticsAlertEvent(
            tenantId, "engagement_drop", "facebook", "reach", "warning", "Reach giảm 40%", OccurredAt);

        evt.TenantId.Should().Be(tenantId);
        evt.AlertType.Should().Be("engagement_drop");
        evt.Platform.Should().Be("facebook");
        evt.Metric.Should().Be("reach");
        evt.Severity.Should().Be("warning");
        evt.Message.Should().Be("Reach giảm 40%");
    }
}

public sealed class DefaultGoldenHourResolverTests
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private readonly DefaultGoldenHourResolver _resolver = new();

    [Theory]
    [InlineData("zalo", 8, 0)]
    [InlineData("youtube", 18, 0)]
    [InlineData("instagram", 19, 30)]
    [InlineData("tiktok", 20, 0)]
    [InlineData("facebook", 20, 30)]
    public void ResolveNext_KnownPlatform_UsesConfiguredHour(string platform, int hour, int minute)
    {
        // 01:00 giờ VN — mọi golden hour đều còn ở phía trước trong cùng ngày.
        var utcNow = new DateTimeOffset(2026, 8, 17, 1, 0, 0, VietnamOffset).ToUniversalTime();

        var next = _resolver.ResolveNext(platform, utcNow);

        next.ToOffset(VietnamOffset).Hour.Should().Be(hour);
        next.ToOffset(VietnamOffset).Minute.Should().Be(minute);
        next.ToOffset(VietnamOffset).Date.Should().Be(new DateTime(2026, 8, 17));
    }

    [Fact]
    public void ResolveNext_IsCaseInsensitiveAndTrims()
    {
        var utcNow = new DateTimeOffset(2026, 8, 17, 1, 0, 0, VietnamOffset).ToUniversalTime();

        var next = _resolver.ResolveNext("  FaceBook  ", utcNow);

        next.ToOffset(VietnamOffset).TimeOfDay.Should().Be(new TimeSpan(20, 30, 0));
    }

    [Fact]
    public void ResolveNext_UnknownPlatform_FallsBackTo19h()
    {
        var utcNow = new DateTimeOffset(2026, 8, 17, 1, 0, 0, VietnamOffset).ToUniversalTime();

        var next = _resolver.ResolveNext("threads", utcNow);

        next.ToOffset(VietnamOffset).TimeOfDay.Should().Be(new TimeSpan(19, 0, 0));
    }

    [Fact]
    public void ResolveNext_GoldenHourAlreadyPassed_RollsToNextDay()
    {
        // 22:00 giờ VN đã qua golden hour facebook (20:30) — phải nhảy sang hôm sau.
        var utcNow = new DateTimeOffset(2026, 8, 17, 22, 0, 0, VietnamOffset).ToUniversalTime();

        var next = _resolver.ResolveNext("facebook", utcNow);

        next.ToOffset(VietnamOffset).Date.Should().Be(new DateTime(2026, 8, 18));
        next.ToOffset(VietnamOffset).TimeOfDay.Should().Be(new TimeSpan(20, 30, 0));
    }
}
