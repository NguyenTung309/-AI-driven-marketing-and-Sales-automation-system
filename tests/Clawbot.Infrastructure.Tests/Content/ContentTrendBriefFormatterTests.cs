using Clawbot.SharedKernel.Content;
using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class ContentTrendBriefFormatterTests
{
    [Fact]
    public void Format_creates_stable_marker_and_parse_round_trips_trend()
    {
        var trend = new ContentTrendBrief(
            WeekOf: "2026-W23",
            Topic: "HSK listening challenge",
            Source: "youtube",
            Metric: "12000 views",
            RelevanceScore: 14.25d,
            ContentIdeas: ["30-second listening drill", "Tone pairs carousel"]);

        var brief = ContentTrendBriefFormatter.Format(trend);

        brief.Should().StartWith("[trend:2026-W23] HSK listening challenge");
        ContentTrendBriefFormatter.TryParse(brief, out var parsed).Should().BeTrue();
        parsed.Should().BeEquivalentTo(trend);
    }

    [Fact]
    public void IsTrendBrief_filters_by_week_when_requested()
    {
        var brief = ContentTrendBriefFormatter.Format(new ContentTrendBrief(
            "2026-W23",
            "Mandarin tones",
            "google_trends",
            "20K+",
            12d,
            []));

        ContentTrendBriefFormatter.IsTrendBrief(brief, "2026-W23").Should().BeTrue();
        ContentTrendBriefFormatter.IsTrendBrief(brief, "2026-W24").Should().BeFalse();
    }

    [Fact]
    public void CurrentWeekOf_uses_gmt_plus_7_boundary()
    {
        var utc = new DateTimeOffset(2026, 1, 4, 18, 30, 0, TimeSpan.Zero);

        ContentTrendBriefFormatter.CurrentWeekOf(utc).Should().Be("2026-W02");
    }
}
