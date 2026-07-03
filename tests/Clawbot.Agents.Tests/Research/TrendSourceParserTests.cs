using Clawbot.Agents.Core.Research;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Agents.Tests.Research;

public sealed class TrendSourceParserTests
{
    [Fact]
    public void Google_trends_rss_parser_reads_topics_and_traffic_metric()
    {
        const string xml = """
            <rss xmlns:ht="https://trends.google.com/trends/trendingsearches/daily">
              <channel>
                <item>
                  <title>HSK exam registration</title>
                  <ht:approx_traffic>20K+</ht:approx_traffic>
                </item>
              </channel>
            </rss>
            """;

        var trends = GoogleTrendsRssSource.ParseRss(xml);

        trends.Should().ContainSingle();
        trends[0].Topic.Should().Be("HSK exam registration");
        trends[0].Source.Should().Be("google_trends");
        trends[0].Metric.Should().Be("20K+");
        trends[0].SourceScore.Should().Be(20_000d);
    }

    [Fact]
    public void Google_trends_rss_parser_reads_current_trending_rss_namespace()
    {
        const string xml = """
            <rss xmlns:ht="https://trends.google.com/trending/rss" version="2.0">
              <channel>
                <item>
                  <title>tây ban nha vs áo</title>
                  <ht:approx_traffic>10,000+</ht:approx_traffic>
                  <description/>
                </item>
              </channel>
            </rss>
            """;

        var trends = GoogleTrendsRssSource.ParseRss(xml);

        trends.Should().ContainSingle();
        trends[0].Topic.Should().Be("tây ban nha vs áo");
        trends[0].Metric.Should().Be("10,000+");
        trends[0].SourceScore.Should().Be(10_000d);
    }

    [Fact]
    public void YouTube_json_parser_reads_video_titles_and_view_counts()
    {
        const string json = """
            {
              "items": [
                {
                  "snippet": {
                    "title": "Learn Chinese tones fast",
                    "tags": ["mandarin", "hsk"]
                  },
                  "statistics": {
                    "viewCount": "12345"
                  }
                }
              ]
            }
            """;

        var trends = YouTubeDataApiSource.ParseVideoList(json);

        trends.Should().ContainSingle();
        trends[0].Topic.Should().Be("Learn Chinese tones fast");
        trends[0].Source.Should().Be("youtube");
        trends[0].Metric.Should().Be("12345 views");
        trends[0].SourceScore.Should().Be(12_345d);
        trends[0].ContentIdeas.Should().Contain("mandarin");
    }

    [Fact]
    public async Task Html_source_returns_empty_when_tenant_override_disables_it()
    {
        using var http = new HttpClient();
        var source = new TikTokScrapeSource(http, Options.Create(new TikTokScrapeOptions { Url = "https://example.com/trends" }));

        var trends = await source.FetchAsync("VN", new TrendSourceOverride(Enabled: false), CancellationToken.None);

        trends.Should().BeEmpty();
    }

    [Fact]
    public async Task Html_source_blocks_private_tenant_url()
    {
        using var http = new HttpClient();
        var source = new TikTokScrapeSource(http, Options.Create(new TikTokScrapeOptions()));

        var trends = await source.FetchAsync(
            "VN",
            new TrendSourceOverride(Enabled: true, Url: "https://localhost/internal"),
            CancellationToken.None);

        trends.Should().BeEmpty();
    }

    [Fact]
    public async Task Html_scrape_parser_reads_data_topic_nodes()
    {
        const string html = """
            <html>
              <body>
                <div data-trend-topic="Chinese New Year vocabulary"></div>
                <article class="trend">HSK 4 listening tips</article>
              </body>
            </html>
            """;

        var trends = await HtmlTrendParser.ParseAsync(html, "tiktok", CancellationToken.None);

        trends.Select(t => t.Topic).Should().Equal("Chinese New Year vocabulary", "HSK 4 listening tips");
        trends.Should().OnlyContain(t => t.Source == "tiktok");
    }
}
