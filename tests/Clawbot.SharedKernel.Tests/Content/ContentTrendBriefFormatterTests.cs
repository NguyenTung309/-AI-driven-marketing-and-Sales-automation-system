using Clawbot.SharedKernel.Content;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content;

// Định dạng + parse brief xu hướng nội dung: chuẩn hoá tuần ISO, marker [trend:...], round-trip Format/TryParse.
public sealed class ContentTrendBriefFormatterTests
{
    private static readonly DateTimeOffset SampleUtc = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentWeekOf_ReturnsIsoWeekInVietnamTime()
    {
        var week = ContentTrendBriefFormatter.CurrentWeekOf(SampleUtc);

        week.Should().MatchRegex(@"^\d{4}-W\d{2}$");
    }

    [Theory]
    [InlineData("2026-W05", "2026-W05")]
    [InlineData(" 2026-w05 ", "2026-W05")]
    [InlineData("0001-W01", "0001-W01")]
    [InlineData("2026-W53", "2026-W53")]
    public void TryNormalizeWeekOf_Valid_ReturnsNormalized(string input, string expected)
    {
        ContentTrendBriefFormatter.TryNormalizeWeekOf(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026W05")]     // thiếu dấu -
    [InlineData("2026-X05")]    // sai ký tự W
    [InlineData("2026-W54")]    // tuần > 53
    [InlineData("2026-W00")]    // tuần < 1
    [InlineData("abcd-W05")]    // năm không phải số
    [InlineData("2026-W5")]     // sai độ dài
    public void TryNormalizeWeekOf_Invalid_ReturnsFalse(string? input)
    {
        ContentTrendBriefFormatter.TryNormalizeWeekOf(input, out var normalized).Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWeekOfOrCurrent_Blank_ReturnsCurrent()
    {
        var result = ContentTrendBriefFormatter.NormalizeWeekOfOrCurrent("  ", SampleUtc);

        result.Should().Be(ContentTrendBriefFormatter.CurrentWeekOf(SampleUtc));
    }

    [Fact]
    public void NormalizeWeekOfOrCurrent_Invalid_Throws()
    {
        var act = () => ContentTrendBriefFormatter.NormalizeWeekOfOrCurrent("garbage", SampleUtc);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Marker_BuildsTrendPrefix()
    {
        ContentTrendBriefFormatter.Marker("2026-W05", "AI in education")
            .Should().Be("[trend:2026-W05] AI in education");
    }

    [Fact]
    public void Marker_InvalidWeek_Throws()
    {
        var act = () => ContentTrendBriefFormatter.Marker("nope", "topic");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Marker_BlankTopic_Throws()
    {
        var act = () => ContentTrendBriefFormatter.Marker("2026-W05", "  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Format_ProducesMarkerSourceMetricScoreIdeas()
    {
        var brief = new ContentTrendBrief(
            "2026-W05", "AI tutoring", "Google Trends", "search volume +40%", 0.875,
            ["Video giới thiệu", "Bài blog so sánh", "   "]);

        var text = ContentTrendBriefFormatter.Format(brief);

        text.Should().StartWith("[trend:2026-W05] AI tutoring");
        text.Should().Contain("Source: Google Trends");
        text.Should().Contain("Metric: search volume +40%");
        text.Should().Contain("Score: 0.875");
        text.Should().Contain("- Video giới thiệu");
        text.Should().Contain("- Bài blog so sánh");
        // Idea trắng bị loại.
        text.Should().NotContain("-    ");
    }

    [Fact]
    public void Format_NullTrend_Throws()
    {
        var act = () => ContentTrendBriefFormatter.Format(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Format_BlankSource_Throws()
    {
        var brief = new ContentTrendBrief("2026-W05", "topic", "  ", "m", 1.0, []);
        var act = () => ContentTrendBriefFormatter.Format(brief);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FormatThenTryParse_RoundTrips()
    {
        var original = new ContentTrendBrief(
            "2026-W12", "Chủ đề hot", "Exa", "mentions", 3.14,
            ["Ý tưởng 1", "Ý tưởng 2"]);

        var text = ContentTrendBriefFormatter.Format(original);
        ContentTrendBriefFormatter.TryParse(text, out var parsed).Should().BeTrue();

        parsed!.WeekOf.Should().Be("2026-W12");
        parsed.Topic.Should().Be("Chủ đề hot");
        parsed.Source.Should().Be("Exa");
        parsed.Metric.Should().Be("mentions");
        parsed.RelevanceScore.Should().BeApproximately(3.14, 0.0001);
        parsed.ContentIdeas.Should().Equal("Ý tưởng 1", "Ý tưởng 2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no marker here")]
    [InlineData("[trend:2026-W05] topic\nMetric: x\nScore: 1.0")] // thiếu Source
    [InlineData("[trend:2026-W05] topic\nSource: s\nScore: notnum")] // score không phải số
    public void TryParse_Invalid_ReturnsFalse(string? brief)
    {
        ContentTrendBriefFormatter.TryParse(brief, out var trend).Should().BeFalse();
        trend.Should().BeNull();
    }

    [Fact]
    public void IsTrendBrief_MatchesMarkerAndOptionalWeek()
    {
        const string brief = "[trend:2026-W05] topic\nSource: s\nScore: 1.0";

        ContentTrendBriefFormatter.IsTrendBrief(brief).Should().BeTrue();
        ContentTrendBriefFormatter.IsTrendBrief(brief, "2026-W05").Should().BeTrue();
        ContentTrendBriefFormatter.IsTrendBrief(brief, "2026-W06").Should().BeFalse();
        ContentTrendBriefFormatter.IsTrendBrief("plain text").Should().BeFalse();
    }
}
