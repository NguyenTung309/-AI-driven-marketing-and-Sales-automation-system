using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clawbot.Agents.Core.Skills.Content;

public sealed record TrendingHashtag(string Tag, long PostCount, double GrowthRate24h);

public interface IHashtagResearcher : ISkill
{
    Task<IReadOnlyList<TrendingHashtag>> TopAsync(string platform, string region, int limit, CancellationToken ct);
}

internal sealed partial class TikTokHashtagResearcher(
    HttpClient http,
    ILogger<TikTokHashtagResearcher> logger) : IHashtagResearcher
{
    public string Name => "hashtag-research-vn";

    private static readonly IReadOnlyList<TrendingHashtag> FallbackVietnamese = new[]
    {
        new TrendingHashtag("#hocbong", 125_000, 0.15),
        new TrendingHashtag("#tiengtrung", 98_000, 0.22),
        new TrendingHashtag("#duhoc", 87_000, 0.08),
        new TrendingHashtag("#lophoc", 65_000, 0.12),
        new TrendingHashtag("#tienganh", 54_000, 0.05),
        new TrendingHashtag("#khoahoc", 48_000, 0.18),
        new TrendingHashtag("#onlinlearning", 42_000, 0.30),
        new TrendingHashtag("#studyabroad", 38_000, 0.10),
        new TrendingHashtag("#giaovien", 31_000, 0.07),
        new TrendingHashtag("#chinhhang", 28_000, 0.25),
    };

    public async Task<IReadOnlyList<TrendingHashtag>> TopAsync(string platform, string region, int limit, CancellationToken ct)
    {
        if (limit <= 0) limit = 10;

        try
        {
            if (platform.Equals("tiktok", StringComparison.OrdinalIgnoreCase))
                return await FetchTikTokAsync(region, limit, ct).ConfigureAwait(false);

            if (platform.Equals("google", StringComparison.OrdinalIgnoreCase))
                return await FetchGoogleTrendsAsync(region, limit, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            LogFetchFailed(logger, platform, ex.Message);
        }

        return FallbackVietnamese.Take(limit).ToList();
    }

    private async Task<IReadOnlyList<TrendingHashtag>> FetchTikTokAsync(string region, int limit, CancellationToken ct)
    {
        var url = $"https://ads.tiktok.com/business/creativecenter/api/hashtag/popular?period=7&country_code={region}&limit={limit}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "Clawbot/1.0");

        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var results = new List<TrendingHashtag>();
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("hashtag_list", out var list))
        {
            foreach (var item in list.EnumerateArray())
            {
                var tag = item.TryGetProperty("hashtag_name", out var n) ? n.GetString() ?? "" : "";
                var count = item.TryGetProperty("publish_cnt", out var c) ? c.GetInt64() : 0;
                var growth = item.TryGetProperty("growth_rate", out var g) ? g.GetDouble() : 0;

                if (!string.IsNullOrWhiteSpace(tag))
                    results.Add(new TrendingHashtag($"#{tag}", count, growth));
            }
        }

        return results.Count > 0 ? results : FallbackVietnamese.Take(limit).ToList();
    }

    private async Task<IReadOnlyList<TrendingHashtag>> FetchGoogleTrendsAsync(string region, int limit, CancellationToken ct)
    {
        var url = $"https://trends.google.com/trending/api/rss?geo={region}";
        var xml = await http.GetStringAsync(url, ct).ConfigureAwait(false);

        var results = new List<TrendingHashtag>();
        foreach (var line in xml.Split('\n'))
        {
            if (!line.Contains("<title>", StringComparison.OrdinalIgnoreCase)) continue;
            var start = line.IndexOf("<title>", StringComparison.OrdinalIgnoreCase) + 7;
            var end = line.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
            if (end <= start) continue;

            var raw = line[start..end].Trim();
            if (string.IsNullOrWhiteSpace(raw) || raw.Equals("Trending searches", StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new TrendingHashtag($"#{raw.Replace(' ', '-')}", 0, 0));
            if (results.Count >= limit) break;
        }

        return results.Count > 0 ? results : FallbackVietnamese.Take(limit).ToList();
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning,
        Message = "Hashtag fetch failed for {Platform}: {Reason}")]
    private static partial void LogFetchFailed(ILogger logger, string platform, string reason);
}
