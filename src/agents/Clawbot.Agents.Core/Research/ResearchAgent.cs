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
    ScoredTrend Score(RawTrend trend, IReadOnlyList<string> keywords);
}

// AI gate: dùng kho tri thức (keywords lấy từ KbModules) làm ngữ cảnh domain để LLM lọc chủ đề
// LIÊN QUAN + viết ý tưởng nội dung tiếng Việt. Trả null khi không dùng được (chưa gắn LLM) → caller
// fallback về keyword scorer.
public interface ITrendCurator
{
    Task<IReadOnlyList<ScoredTrend>?> CurateAsync(
        Guid tenantId,
        IReadOnlyList<RawTrend> candidates,
        IReadOnlyList<string> keywords,
        CancellationToken ct = default);
}

public interface IResearchAgent
{
    Task<IReadOnlyList<ScoredTrend>> ScanAsync(ResearchScanRequest request, CancellationToken ct = default);
}

internal sealed class WeightedTrendScorer : ITrendRelevanceScorer
{
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

    public ScoredTrend Score(RawTrend trend, IReadOnlyList<string> keywords)
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
    ITrendRelevanceScorer scorer,
    ITrendCurator? curator = null) : IResearchAgent
{
    private const int MaxResults = 25;
    private readonly IReadOnlyList<ITrendSource> _sources = sources.ToList();
    private readonly ITrendRelevanceScorer _scorer = scorer;
    private readonly ITrendCurator? _curator = curator;

    public async Task<IReadOnlyList<ScoredTrend>> ScanAsync(ResearchScanRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // No Enabled prefilter: per-tenant overrides can enable a source that is off globally
        // (e.g. a tenant-scoped YouTube key), so each source decides inside FetchAsync.
        var tasks = _sources
            .Select(source => FetchSourceAsync(source, request.Geo, OverrideFor(source.Source, request.Overrides), ct))
            .ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var candidates = results
            .SelectMany(r => r)
            .GroupBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(t => t.SourceScore).First())
            .ToList();
        if (candidates.Count == 0)
            return [];

        // AI gate dựa trên kho tri thức (keywords từ KbModules): giữ đúng chủ đề liên quan domain.
        if (_curator is not null)
        {
            var curated = await _curator.CurateAsync(request.TenantId, candidates, request.Keywords, ct).ConfigureAwait(false);
            if (curated is not null)
                return curated
                    .OrderByDescending(t => t.RelevanceScore)
                    .ThenBy(t => t.Topic)
                    .Take(MaxResults)
                    .ToList();
        }

        // Fallback (chưa gắn LLM): keyword scorer — chỉ giữ chủ đề khớp keyword của tenant.
        return candidates
            .Select(t => _scorer.Score(t, request.Keywords))
            .Where(t => t.RelevanceScore > 0d)
            .OrderByDescending(t => t.RelevanceScore)
            .ThenBy(t => t.Topic)
            .Take(MaxResults)
            .ToList();
    }

    private static TrendSourceOverride? OverrideFor(string source, TrendOverrides? overrides) => source switch
    {
        "google_trends" => overrides?.GoogleTrends,
        "youtube" => overrides?.YouTube,
        "tiktok" => overrides?.TikTok,
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
