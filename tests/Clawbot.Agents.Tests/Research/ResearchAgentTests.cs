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
    public async Task ResearchAgent_disables_hidden_sources_and_skips_other_source_failures()
    {
        var google = new FakeTrendSource(
            "google_trends",
            enabled: true,
            [new RawTrend("HSK 5 vocabulary", "google_trends", "2K+", 2_000d, [])]);
        var youtube = new FakeTrendSource(
            "youtube",
            enabled: true,
            [new RawTrend("HSK YouTube trend", "youtube", "10K views", 10_000d, [])]);
        var tiktok = new FakeTrendSource(
            "tiktok",
            enabled: true,
            [new RawTrend("HSK TikTok trend", "tiktok", "20K views", 20_000d, [])]);
        var failingSource = new FakeTrendSource("baidu", enabled: true, null, throwOnFetch: true);
        var agent = new ResearchAgent([google, youtube, tiktok, failingSource], new WeightedTrendScorer());

        var trends = await agent.ScanAsync(
            new ResearchScanRequest(Guid.NewGuid(), "VN", ["HSK"]),
            CancellationToken.None);

        trends.Should().ContainSingle();
        trends[0].Topic.Should().Be("HSK 5 vocabulary");
        google.FetchCount.Should().Be(1);
        youtube.FetchCount.Should().Be(0);
        tiktok.FetchCount.Should().Be(0);
        failingSource.FetchCount.Should().Be(1);
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

        public Task<IReadOnlyList<RawTrend>> FetchAsync(string geo, TrendSourceOverride? tenantOverride = null, CancellationToken ct = default)
        {
            _ = geo;
            _ = ct;
            // Mirrors real sources: a disabled source self-guards inside FetchAsync and does not fetch.
            if (!(tenantOverride?.Enabled ?? enabled))
                return Task.FromResult<IReadOnlyList<RawTrend>>([]);

            FetchCount++;
            if (throwOnFetch)
                throw new HttpRequestException("source down");

            return Task.FromResult(trends ?? []);
        }
    }
}
