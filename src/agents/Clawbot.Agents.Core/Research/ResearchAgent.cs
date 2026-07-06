using System.Globalization;

namespace Clawbot.Agents.Core.Research;

public sealed record ScoredTrend(
    string Topic,
    string Source,
    string Metric,
    double RelevanceScore,
    IReadOnlyList<string> ContentIdeas);

public sealed record ResearchScanRequest(Guid TenantId, string Geo, IReadOnlyList<string> Keywords);

public interface ITrendRelevanceScorer
{
    Task<IReadOnlyList<ScoredTrend>> ScoreAsync(
        Guid tenantId,
        IReadOnlyList<RawTrend> trends,
        IReadOnlyList<string> keywords,
        CancellationToken ct = default);
}

public interface IResearchAgent
{
    Task<IReadOnlyList<ScoredTrend>> ScanAsync(ResearchScanRequest request, CancellationToken ct = default);
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

        var haystack = string.Join(' ', new[] { trend.Topic }.Concat(trend.ContentIdeas))
            .ToLower(CultureInfo.InvariantCulture);
        var matches = allKeywords.Count(k => haystack.Contains(k.ToLower(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var keywordScore = matches * 10d;
        var sourceScore = Math.Log10(Math.Max(1d, trend.SourceScore) + 1d);
        var score = Math.Round(keywordScore + sourceScore, 4);
        var ideas = trend.ContentIdeas.Count == 0
            ? [$"Turn '{trend.Topic}' into a Chinese-learning content brief"]
            : trend.ContentIdeas;

        return new ScoredTrend(trend.Topic, trend.Source, trend.Metric, score, ideas);
    }
}

internal sealed class ResearchAgent(IEnumerable<ITrendSource> sources, ITrendRelevanceScorer scorer) : IResearchAgent
{
    private readonly IReadOnlyList<ITrendSource> _sources = sources.ToList();
    private readonly ITrendRelevanceScorer _scorer = scorer;

    public async Task<IReadOnlyList<ScoredTrend>> ScanAsync(ResearchScanRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var enabled = _sources.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0)
            return [];

        var tasks = enabled.Select(source => FetchSourceAsync(source, request.Geo, ct)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var deduped = results
            .SelectMany(r => r)
            .GroupBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(t => t.SourceScore).First())
            .ToList();

        var scored = await _scorer.ScoreAsync(request.TenantId, deduped, request.Keywords, ct).ConfigureAwait(false);
        return scored
            .Where(t => t.RelevanceScore > 0d)
            .OrderByDescending(t => t.RelevanceScore)
            .ThenBy(t => t.Topic)
            .Take(25)
            .ToList();
    }

    private static async Task<IReadOnlyList<RawTrend>> FetchSourceAsync(
        ITrendSource source,
        string geo,
        CancellationToken ct)
    {
        try
        {
            return await source.FetchAsync(geo, ct).ConfigureAwait(false);
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
