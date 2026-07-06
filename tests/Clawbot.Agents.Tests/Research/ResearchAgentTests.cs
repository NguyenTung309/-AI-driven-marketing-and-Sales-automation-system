using Clawbot.Agents.Core.Research;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Research;

public sealed class ResearchAgentTests
{
    [Fact]
    public void WeightedTrendScorer_ranks_keyword_matches_above_generic_trends()
    {
        var keywords = new[] { "HSK", "tiếng Trung", "Mandarin" };

        var relevant = WeightedTrendScorer.Score(
            new RawTrend("HSK speaking tips", "youtube", "100 views", 100d, ["mandarin"]),
            keywords);
        var generic = WeightedTrendScorer.Score(
            new RawTrend("celebrity gossip", "youtube", "10000 views", 10_000d, []),
            keywords);

        relevant.RelevanceScore.Should().BeGreaterThan(generic.RelevanceScore);
        relevant.ContentIdeas.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResearchAgent_fans_out_sources_and_skips_failures()
    {
        var sourceA = new FakeTrendSource(
            "google_trends",
            enabled: true,
            [new RawTrend("HSK 5 vocabulary", "google_trends", "2K+", 2_000d, [])]);
        var sourceB = new FakeTrendSource("youtube", enabled: true, null, throwOnFetch: true);
        var sourceC = new FakeTrendSource("baidu", enabled: false, [new RawTrend("drop", "baidu", "", 1d, [])]);
        var agent = new ResearchAgent([sourceA, sourceB, sourceC], new WeightedTrendScorer());

        var trends = await agent.ScanAsync(
            new ResearchScanRequest(Guid.NewGuid(), "VN", ["HSK"]),
            CancellationToken.None);

        trends.Should().ContainSingle();
        trends[0].Topic.Should().Be("HSK 5 vocabulary");
        sourceA.FetchCount.Should().Be(1);
        sourceB.FetchCount.Should().Be(1);
        sourceC.FetchCount.Should().Be(0);
    }

    private sealed class FakeTrendSource(
        string source,
        bool enabled,
        IReadOnlyList<RawTrend>? trends,
        bool throwOnFetch = false) : ITrendSource
    {
        public int FetchCount { get; private set; }
        public string Source => source;
        public bool Enabled => enabled;

        public Task<IReadOnlyList<RawTrend>> FetchAsync(string geo, CancellationToken ct = default)
        {
            _ = geo;
            _ = ct;
            FetchCount++;
            if (throwOnFetch)
                throw new HttpRequestException("source down");

            return Task.FromResult(trends ?? []);
        }
    }
}
