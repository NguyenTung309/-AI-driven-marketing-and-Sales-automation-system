using Clawbot.Api.Contracts.Analytics;
using Clawbot.Api.Services;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class AnalyticsAggregationTests
{
    [Fact]
    public void BuildOmniRows_groups_platforms_and_calculates_cpl()
    {
        var rows = new[]
        {
            new KpiDailyDto(new DateOnly(2026, 6, 1), "facebook", 10, 4, 3, 1, 100m, 50m, 5m),
            new KpiDailyDto(new DateOnly(2026, 6, 2), "facebook", 5, 2, 1, 0, 200m, 25m, 5m),
            new KpiDailyDto(new DateOnly(2026, 6, 1), "youtube", 0, 1, 0, 0, null, null, null),
        };

        var result = AnalyticsAggregationService.BuildOmniRows(rows);

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new OmniChannelRowDto(
            "facebook", 15, 6, 4, 1, 150m, 75m, 5m));
        result[1].Should().BeEquivalentTo(new OmniChannelRowDto(
            "youtube", 0, 1, 0, 0, null, null, null));
    }

    [Fact]
    public void IsFreshForecast_rejects_rows_older_than_24_hours()
    {
        var now = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

        AnalyticsAggregationService.IsFreshForecast(now.AddHours(-23), now).Should().BeTrue();
        AnalyticsAggregationService.IsFreshForecast(now.AddHours(-25), now).Should().BeFalse();
    }
}
