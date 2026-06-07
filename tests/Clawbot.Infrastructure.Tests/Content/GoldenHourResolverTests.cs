using Clawbot.SharedKernel.Content;
using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class GoldenHourResolverTests
{
    [Fact]
    public void ResolveNext_returns_today_platform_hour_when_it_is_still_future_in_gmt7()
    {
        var resolver = new DefaultGoldenHourResolver();
        var nowUtc = new DateTimeOffset(2026, 6, 7, 8, 0, 0, TimeSpan.Zero);

        var resolved = resolver.ResolveNext("tiktok", nowUtc);

        resolved.Should().Be(new DateTimeOffset(2026, 6, 7, 20, 0, 0, TimeSpan.FromHours(7)));
    }

    [Fact]
    public void ResolveNext_rolls_to_tomorrow_when_today_hour_has_passed()
    {
        var resolver = new DefaultGoldenHourResolver();
        var nowUtc = new DateTimeOffset(2026, 6, 7, 14, 0, 0, TimeSpan.Zero);

        var resolved = resolver.ResolveNext("zalo", nowUtc);

        resolved.Should().Be(new DateTimeOffset(2026, 6, 8, 8, 0, 0, TimeSpan.FromHours(7)));
    }
}
