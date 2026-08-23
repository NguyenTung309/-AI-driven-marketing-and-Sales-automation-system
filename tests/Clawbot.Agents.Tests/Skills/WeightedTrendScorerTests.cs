using Clawbot.Agents.Core.Research;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Heuristic trend scorer: chỉ khớp keyword trên Topic; không khớp => score 0; default keyword tiếng Trung.
public sealed class WeightedTrendScorerTests
{
    private static RawTrend Trend(string topic, double sourceScore = 10d, IReadOnlyList<string>? ideas = null)
        => new(topic, "google_trends", "rising", sourceScore, ideas ?? Array.Empty<string>());

    [Fact]
    public void Score_NullTrend_Throws()
    {
        var act = () => WeightedTrendScorer.Score(null!, Array.Empty<string>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Score_NullKeywords_Throws()
    {
        var act = () => WeightedTrendScorer.Score(Trend("x"), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Score_NoKeywordMatch_ZeroScore()
    {
        var scored = WeightedTrendScorer.Score(Trend("world cup final"), Array.Empty<string>());

        scored.RelevanceScore.Should().Be(0d);
    }

    [Fact]
    public void Score_DefaultChineseKeyword_Matches()
    {
        // "tiếng trung" nằm trong DefaultKeywords => khớp dù caller không truyền keyword.
        var scored = WeightedTrendScorer.Score(Trend("khoá tiếng trung online"), Array.Empty<string>());

        scored.RelevanceScore.Should().BeGreaterThan(0d);
    }

    [Fact]
    public void Score_CustomKeyword_Matches()
    {
        var scored = WeightedTrendScorer.Score(Trend("luyện thi HSK5"), new[] { "hsk5" });

        scored.RelevanceScore.Should().BeGreaterThan(0d);
    }

    [Fact]
    public void Score_MoreMatches_HigherScore()
    {
        var one = WeightedTrendScorer.Score(Trend("chinese class"), new[] { "chinese" });
        var two = WeightedTrendScorer.Score(Trend("chinese mandarin class"), new[] { "chinese", "mandarin" });

        two.RelevanceScore.Should().BeGreaterThan(one.RelevanceScore);
    }

    [Fact]
    public void Score_EmptyIdeas_GetsGeneratedIdea()
    {
        var scored = WeightedTrendScorer.Score(Trend("tiếng trung", ideas: Array.Empty<string>()), Array.Empty<string>());

        scored.ContentIdeas.Should().ContainSingle();
        scored.ContentIdeas[0].Should().Contain("tiếng trung");
    }

    [Fact]
    public void Score_ExistingIdeas_Preserved()
    {
        var ideas = new[] { "idea A", "idea B" };
        var scored = WeightedTrendScorer.Score(Trend("tiếng trung", ideas: ideas), Array.Empty<string>());

        scored.ContentIdeas.Should().BeEquivalentTo(ideas);
    }

    [Fact]
    public void Score_PreservesTopicSourceMetric()
    {
        var scored = WeightedTrendScorer.Score(Trend("tiếng trung"), Array.Empty<string>());

        scored.Topic.Should().Be("tiếng trung");
        scored.Source.Should().Be("google_trends");
        scored.Metric.Should().Be("rising");
    }
}
