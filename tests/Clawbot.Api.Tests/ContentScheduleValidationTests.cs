using Clawbot.Api.Contracts.Content;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Content;
using Clawbot.SharedKernel.Content;
using FluentAssertions;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class ContentScheduleValidationTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ResolveScheduledAt_uses_golden_hour_when_manual_time_is_missing()
    {
        var item = ContentItem.Create(TenantId, "tiktok", "Body", createdBy: null, Now);

        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(null),
            item,
            Now,
            new DefaultGoldenHourResolver());

        result.ErrorCode.Should().BeNull();
        result.ScheduledAt.Should().Be(new DateTimeOffset(2026, 6, 8, 20, 0, 0, TimeSpan.FromHours(7)));
    }

    [Fact]
    public void ResolveScheduledAt_rejects_manual_time_in_the_past()
    {
        var item = ContentItem.Create(TenantId, "facebook", "Body", createdBy: null, Now);

        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(Now.AddMinutes(-1)),
            item,
            Now,
            new DefaultGoldenHourResolver());

        result.ErrorCode.Should().Be("content.schedule_in_past");
    }

    [Fact]
    public void ResolveScheduledAt_accepts_valid_future_time()
    {
        var item = ContentItem.Create(TenantId, "youtube", "Body", createdBy: null, Now);
        var future = Now.AddHours(3);

        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(future),
            item,
            Now,
            new DefaultGoldenHourResolver());

        result.ErrorCode.Should().BeNull();
        result.ScheduledAt.Should().Be(future);
    }

    [Fact]
    public void ResolveScheduledAt_uses_default_golden_hour_for_unknown_platform()
    {
        var item = ContentItem.Create(TenantId, "unknown-platform", "Body", createdBy: null, Now);

        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(null),
            item,
            Now,
            new DefaultGoldenHourResolver());

        result.ErrorCode.Should().BeNull();
        result.ScheduledAt.Offset.Should().Be(TimeSpan.FromHours(7));
        result.ScheduledAt.Hour.Should().Be(19);
    }
}
