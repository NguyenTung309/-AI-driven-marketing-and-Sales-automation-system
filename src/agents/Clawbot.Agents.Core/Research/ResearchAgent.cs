using System.Globalization;

namespace Clawbot.Agents.Core.Research;

public sealed record ScoredTrend(
    string Topic,
    string Source,
    string Metric,
    double RelevanceScore,
    IReadOnlyList<string> ContentIdeas);

public sealed record ResearchScanRequest(
    Guid TenantId,
    string Geo,
    IReadOnlyList<string> Keywords,
    TrendOverrides? Overrides = null);

public interface ITrendRelevanceScorer
{
    Task<IReadOnlyList<ScoredTrend>> ScoreAsync(
        Guid tenantId,
        IReadOnlyList<RawTrend> trends,
        IReadOnlyList<string> keywords,
        CancellationToken ct = default);
}

public sealed record ResearchScanResult(
    IReadOnlyList<ScoredTrend> Trends,
    IReadOnlyList<RawTrend> RawTrends);

public interface IResearchAgent
{
    Task<IReadOnlyList<ScoredTrend>> ScanAsync(ResearchScanRequest request, CancellationToken ct = default);
    Task<ResearchScanResult> ScanWithRawAsync(ResearchScanRequest request, CancellationToken ct = default);
}

// Heuristic keyword scorer: fallback khi tenant/host chua cau hinh LLM + embedding
// (SemanticLlmTrendScorer la duong chinh).
internal sealed class WeightedTrendScorer : ITrendRelevanceScorer
{
    public Task<IReadOnlyList<ScoredTrend>> ScoreAsync(
        Guid tenantId,
        IReadOnlyList<RawTrend> trends,
        IReadOnlyList<string> keywords,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trends);
        IReadOnlyList<ScoredTrend> scored = trends.Select(t => Score(t, keywords)).ToList();
        return Task.FromResult(scored);
    }

    private static readonly string[] DefaultKeywords =
    [
        "hsk",
        "tiếng trung",
        "tieng trung",
        "chinese",
        "mandarin",
        "中文",
        "汉语",
    ];

    public static ScoredTrend Score(RawTrend trend, IReadOnlyList<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(keywords);

        var allKeywords = keywords
            .Concat(DefaultKeywords)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Chỉ khớp trên TOPIC — KHÔNG lấy ContentIdeas vào haystack (idea auto-gán chứa "Chinese" khiến
        // mọi chủ đề đều khớp keyword, đó là lý do trước đây quét ra toàn kết quả không liên quan).
        var haystack = trend.Topic.ToLower(CultureInfo.InvariantCulture);
        var matches = allKeywords.Count(k => haystack.Contains(k.ToLower(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        // Không khớp keyword nào → score 0 → bị loại ở ScanAsync (thay vì lọt qua nhờ sourceScore).
        var sourceScore = Math.Log10(Math.Max(1d, trend.SourceScore) + 1d);
        var score = matches == 0 ? 0d : Math.Round(matches * 10d + sourceScore, 4);
        var ideas = trend.ContentIdeas.Count == 0
            ? [$"Biến '{trend.Topic}' thành brief nội dung học tiếng Trung"]
            : trend.ContentIdeas;

        return new ScoredTrend(trend.Topic, trend.Source, trend.Metric, score, ideas);
    }
}

internal sealed class ResearchAgent(
    IEnumerable<ITrendSource> sources,
    ITrendRelevanceScorer scorer) : IResearchAgent
{
    private const int MaxResults = 25;
    private static readonly TrendSourceOverride HiddenSourceDisabled = new(Enabled: false);
    private readonly IReadOnlyList<ITrendSource> _sources = sources.ToList();
    private readonly ITrendRelevanceScorer _scorer = scorer;

    public async Task<IReadOnlyList<ScoredTrend>> ScanAsync(ResearchScanRequest request, CancellationToken ct = default) =>
        (await ScanWithRawAsync(request, ct).ConfigureAwait(false)).Trends;

    public async Task<ResearchScanResult> ScanWithRawAsync(ResearchScanRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // No Enabled prefilter: per-tenant overrides can enable a source that is off globally
        // (e.g. a tenant-scoped YouTube key), so each source decides inside FetchAsync.
        var tasks = _sources
            .Select(source => FetchSourceAsync(source, request.Geo, OverrideFor(source.Source, request.Overrides), ct))
            .ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var deduped = results
            .SelectMany(r => r)
            .GroupBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(t => t.SourceScore).First())
            .ToList();

        var scored = await _scorer.ScoreAsync(request.TenantId, deduped, request.Keywords, ct).ConfigureAwait(false);
        var filtered = scored
            .Where(t => t.RelevanceScore > 0d)
            .OrderByDescending(t => t.RelevanceScore)
            .ThenBy(t => t.Topic)
            .Take(MaxResults)
            .ToList();

        // Fallback: khi nguồn trend tĩnh (Google Trends) không có từ khóa ngách, quét tin tức web theo keywords
        if (filtered.Count == 0 && request.Keywords.Count > 0)
        {
            var keywordSources = _sources.OfType<IKeywordTrendSource>().ToList();
            if (keywordSources.Count > 0)
            {
                var kwTasks = keywordSources
                    .Select(s => s.FetchByKeywordsAsync(request.Geo, request.Keywords, OverrideFor(s.Source, request.Overrides), ct))
                    .ToList();
                var kwResults = await Task.WhenAll(kwTasks).ConfigureAwait(false);
                var kwDeduped = kwResults
                    .SelectMany(r => r)
                    .GroupBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(t => t.SourceScore).First())
                    .ToList();

                if (kwDeduped.Count > 0)
                {
                    var kwScored = await _scorer.ScoreAsync(request.TenantId, kwDeduped, request.Keywords, ct).ConfigureAwait(false);
                    filtered = kwScored
                        .Where(t => t.RelevanceScore > 0d)
                        .OrderByDescending(t => t.RelevanceScore)
                        .ThenBy(t => t.Topic)
                        .Take(MaxResults)
                        .ToList();

                    deduped = deduped.Concat(kwDeduped)
                        .GroupBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(t => t.SourceScore).First())
                        .ToList();
                }
            }
        }

        var raw = deduped
            .OrderByDescending(t => t.SourceScore)
            .ThenBy(t => t.Topic)
            .Take(200)
            .ToList();
        return new ResearchScanResult(filtered, raw);
    }

    private static TrendSourceOverride? OverrideFor(string source, TrendOverrides? overrides) =>
        source.Trim().ToLowerInvariant() switch
        {
            "google_trends" => overrides?.GoogleTrends,
            "youtube" or "tiktok" => HiddenSourceDisabled,
            _ => null,
        };

    private static async Task<IReadOnlyList<RawTrend>> FetchSourceAsync(
        ITrendSource source,
        string geo,
        TrendSourceOverride? overrides,
        CancellationToken ct)
    {
        try
        {
            return await source.FetchAsync(geo, overrides, ct).ConfigureAwait(false);
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
    }
}
