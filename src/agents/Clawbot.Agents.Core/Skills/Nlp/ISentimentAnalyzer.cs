namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record SentimentResult(string Polarity, float Confidence);

public interface ISentimentAnalyzer : ISkill
{
    Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct);
}

// Baseline lexicon scoring. Vendor swap target: wonrax/phobert-base-vietnamese-sentiment.
internal sealed class LexiconSentimentAnalyzer : ISentimentAnalyzer
{
    private static readonly string[] PositiveWords =
        { "tốt", "tuyệt", "hay", "thích", "ok", "great", "good", "love", "thanks", "cảm ơn", "好", "棒" };
    private static readonly string[] NegativeWords =
        { "tệ", "kém", "chán", "không thích", "bực", "bad", "hate", "terrible", "差", "烂" };

    public string Name => "sentiment-analysis";

    public Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new SentimentResult("neutral", 0f));

        var lower = text.ToUpperInvariant();
        var pos = PositiveWords.Count(w => lower.Contains(w.ToUpperInvariant(), StringComparison.Ordinal));
        var neg = NegativeWords.Count(w => lower.Contains(w.ToUpperInvariant(), StringComparison.Ordinal));

        if (pos == 0 && neg == 0)
            return Task.FromResult(new SentimentResult("neutral", 0.40f));
        if (pos > neg)
            return Task.FromResult(new SentimentResult("positive", Math.Min(0.50f + 0.10f * pos, 0.90f)));
        if (neg > pos)
            return Task.FromResult(new SentimentResult("negative", Math.Min(0.50f + 0.10f * neg, 0.90f)));
        return Task.FromResult(new SentimentResult("neutral", 0.45f));
    }
}
