using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using AngleSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Research;

public sealed record RawTrend(
    string Topic,
    string Source,
    string Metric,
    double SourceScore,
    IReadOnlyList<string> ContentIdeas);

// Per-tenant override for one source; null members fall back to the appsettings-bound options.
public sealed record TrendSourceOverride(bool? Enabled = null, string? ApiKey = null, string? Url = null);

public sealed record TrendOverrides(
    TrendSourceOverride? GoogleTrends = null,
    TrendSourceOverride? YouTube = null,
    TrendSourceOverride? TikTok = null);

public interface ITrendSource
{
    string Source { get; }
    bool Enabled { get; }
    Task<IReadOnlyList<RawTrend>> FetchAsync(string geo, TrendSourceOverride? tenantOverride = null, CancellationToken ct = default);
}

public abstract class TrendSourceOptions
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class GoogleTrendsOptions : TrendSourceOptions
{
    public const string SectionName = "Content:Trends:GoogleTrends";

    public string UrlTemplate { get; set; } =
        "https://trends.google.com/trending/rss?geo={geo}";
}

public sealed class YouTubeTrendOptions : TrendSourceOptions
{
    public const string SectionName = "Content:Trends:YouTube";

    public string ApiKey { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 25;
    public string UrlTemplate { get; set; } =
        "https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics&chart=mostPopular&regionCode={geo}&maxResults={maxResults}&key={apiKey}";
}

public class HtmlTrendOptions : TrendSourceOptions
{
    public string Url { get; set; } = string.Empty;
}

public sealed class TikTokScrapeOptions : HtmlTrendOptions
{
    public const string SectionName = "Content:Trends:TikTok";
}

public sealed class BaiduScrapeOptions : HtmlTrendOptions
{
    public const string SectionName = "Content:Trends:Baidu";

    public BaiduScrapeOptions()
    {
        Enabled = false;
    }
}

internal sealed class GoogleTrendsRssSource(HttpClient http, IOptions<GoogleTrendsOptions> options)
    : ITrendSource
{
    private readonly GoogleTrendsOptions _options = options.Value;

    public string Source => "google_trends";
    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyList<RawTrend>> FetchAsync(string geo, TrendSourceOverride? tenantOverride = null, CancellationToken ct = default)
    {
        if (!(tenantOverride?.Enabled ?? _options.Enabled))
            return [];

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            var url = _options.UrlTemplate.Replace("{geo}", Uri.EscapeDataString(geo), StringComparison.Ordinal);
            var xml = await http.GetStringAsync(new Uri(url, UriKind.Absolute), timeout.Token).ConfigureAwait(false);
            return ParseRss(xml);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<RawTrend> ParseRss(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        var doc = XDocument.Parse(xml);
        // Google retired /trends/trendingsearches/daily/rss (404 since Feb 2025); the replacement
        // /trending/rss feed uses a different ht namespace, so probe both.
        XNamespace ht = "https://trends.google.com/trending/rss";
        XNamespace htLegacy = "https://trends.google.com/trends/trendingsearches/daily";
        return doc.Descendants("item")
            .Select(item =>
            {
                var title = (string?)item.Element("title") ?? string.Empty;
                var metric = (string?)item.Element(ht + "approx_traffic")
                    ?? (string?)item.Element(htLegacy + "approx_traffic")
                    ?? (string?)item.Element("description")
                    ?? string.Empty;
                return ToTrend(title, "google_trends", metric);
            })
            .Where(t => !string.IsNullOrWhiteSpace(t.Topic))
            .DistinctBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Không auto-gán ContentIdeas nữa: template "Explain X for Vietnamese Chinese learners" chứa "Chinese"
    // khiến MỌI chủ đề khớp keyword và lọt qua bộ lọc. Ý tưởng nội dung do AI curator / scorer sinh sau.
    private static RawTrend ToTrend(string topic, string source, string metric) =>
        new(topic.Trim(), source, metric.Trim(), ParseMetricScore(metric), []);

    internal static double ParseMetricScore(string metric)
    {
        if (string.IsNullOrWhiteSpace(metric))
            return 0d;

        var normalized = metric.Trim().Replace("+", string.Empty, StringComparison.Ordinal);
        var multiplier = normalized.EndsWith("K", StringComparison.OrdinalIgnoreCase) ? 1_000d
            : normalized.EndsWith("M", StringComparison.OrdinalIgnoreCase) ? 1_000_000d
            : 1d;
        if (multiplier > 1d)
            normalized = normalized[..^1];

        normalized = normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value * multiplier
            : 0d;
    }

}

internal sealed class YouTubeDataApiSource(HttpClient http, IOptions<YouTubeTrendOptions> options)
    : ITrendSource
{
    private readonly YouTubeTrendOptions _options = options.Value;

    public string Source => "youtube";

    // Key may arrive per-tenant at fetch time, so a missing global key no longer disables the source here.
    public bool Enabled => _options.Enabled;

    public async Task<IReadOnlyList<RawTrend>> FetchAsync(string geo, TrendSourceOverride? tenantOverride = null, CancellationToken ct = default)
    {
        var apiKey = tenantOverride?.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = _options.ApiKey;
        if (!(tenantOverride?.Enabled ?? _options.Enabled) || string.IsNullOrWhiteSpace(apiKey))
            return [];

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            var url = _options.UrlTemplate
                .Replace("{geo}", Uri.EscapeDataString(geo), StringComparison.Ordinal)
                .Replace("{maxResults}", _options.MaxResults.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{apiKey}", Uri.EscapeDataString(apiKey), StringComparison.Ordinal);
            var json = await http.GetStringAsync(new Uri(url, UriKind.Absolute), timeout.Token).ConfigureAwait(false);
            return ParseVideoList(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<RawTrend> ParseVideoList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var trends = new List<RawTrend>();
        foreach (var item in items.EnumerateArray())
        {
            var snippet = item.TryGetProperty("snippet", out var sn) ? sn : default;
            var title = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("title", out var titleProp)
                ? titleProp.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var viewCount = 0d;
            if (item.TryGetProperty("statistics", out var stats)
                && stats.TryGetProperty("viewCount", out var views)
                && double.TryParse(views.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                viewCount = parsed;
            }

            var ideas = ReadTags(snippet);
            trends.Add(new RawTrend(title.Trim(), "youtube", $"{viewCount:0} views", viewCount, ideas));
        }

        return trends
            .DistinctBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ReadTags(JsonElement snippet)
    {
        if (snippet.ValueKind != JsonValueKind.Object
            || !snippet.TryGetProperty("tags", out var tags)
            || tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return tags.EnumerateArray()
            .Select(tag => tag.GetString())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!.Trim())
            .Take(8)
            .ToList();
    }
}

internal sealed class TikTokScrapeSource(HttpClient http, IOptions<TikTokScrapeOptions> options)
    : HtmlTrendSource<TikTokScrapeOptions>(http, options, "tiktok");

internal sealed class BaiduScrapeSource(HttpClient http, IOptions<BaiduScrapeOptions> options)
    : HtmlTrendSource<BaiduScrapeOptions>(http, options, "baidu");

internal abstract class HtmlTrendSource<TOptions>(
    HttpClient http,
    IOptions<TOptions> options,
    string sourceName) : ITrendSource
    where TOptions : HtmlTrendOptions
{
    private readonly TOptions _options = options.Value;

    public string Source => sourceName;
    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Url);

    public async Task<IReadOnlyList<RawTrend>> FetchAsync(string geo, TrendSourceOverride? tenantOverride = null, CancellationToken ct = default)
    {
        _ = geo;
        var url = tenantOverride?.Url;
        if (string.IsNullOrWhiteSpace(url))
            url = _options.Url;
        if (!(tenantOverride?.Enabled ?? _options.Enabled) || string.IsNullOrWhiteSpace(url))
            return [];

        // Tenant-supplied URLs must pass the SSRF guard (public https hosts only); appsettings URLs are operator-owned.
        if (!string.Equals(url, _options.Url, StringComparison.OrdinalIgnoreCase)
            && !Chat.LlmBaseUrlGuard.IsAllowedBaseUrl(url))
        {
            return [];
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            var html = await http.GetStringAsync(new Uri(url, UriKind.Absolute), timeout.Token).ConfigureAwait(false);
            return await HtmlTrendParser.ParseAsync(html, sourceName, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (AngleSharp.Dom.DomException)
        {
            return [];
        }
    }
}

internal static class HtmlTrendParser
{
    public static async Task<IReadOnlyList<RawTrend>> ParseAsync(string html, string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var context = BrowsingContext.New(AngleSharp.Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct).ConfigureAwait(false);
        var topics = document.QuerySelectorAll("[data-trend-topic], [data-topic], .trend, .trending, .title, h2, h3")
            .Select(node => node.GetAttribute("data-trend-topic")
                ?? node.GetAttribute("data-topic")
                ?? node.TextContent)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .Where(text => text.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(topic => new RawTrend(topic, source, "scrape", 1d, []))
            .ToList();

        return topics;
    }
}

public static class ResearchModule
{
    public static IServiceCollection AddClawbotResearch(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<GoogleTrendsOptions>(configuration.GetSection(GoogleTrendsOptions.SectionName));
        services.Configure<YouTubeTrendOptions>(configuration.GetSection(YouTubeTrendOptions.SectionName));
        services.Configure<TikTokScrapeOptions>(configuration.GetSection(TikTokScrapeOptions.SectionName));
        services.Configure<BaiduScrapeOptions>(configuration.GetSection(BaiduScrapeOptions.SectionName));

        services.AddHttpClient<GoogleTrendsRssSource>();
        services.AddHttpClient<YouTubeDataApiSource>();
        services.AddHttpClient<TikTokScrapeSource>();
        services.AddHttpClient<BaiduScrapeSource>();
        services.AddScoped<ITrendSource>(sp => sp.GetRequiredService<GoogleTrendsRssSource>());
        services.AddScoped<ITrendSource>(sp => sp.GetRequiredService<YouTubeDataApiSource>());
        services.AddScoped<ITrendSource>(sp => sp.GetRequiredService<TikTokScrapeSource>());
        services.AddScoped<ITrendSource>(sp => sp.GetRequiredService<BaiduScrapeSource>());
        services.AddSingleton<ITrendRelevanceScorer, WeightedTrendScorer>();
        // "Quét" gọi research-agent dùng kho tri thức + LLM lọc chủ đề liên quan; cần IClaudeChatClient
        // (do AddClawbotChat đăng ký). Không gắn LLM → curator tự trả null → ResearchAgent fallback keyword.
        services.AddScoped<ITrendCurator, AiTrendCurator>();
        services.AddScoped<IResearchAgent, ResearchAgent>();
        return services;
    }
}
