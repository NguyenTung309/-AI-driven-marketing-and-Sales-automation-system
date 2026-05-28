namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record SentimentResult(string Polarity, float Confidence);   // negative|neutral|positive

public interface ISentimentAnalyzer : ISkill
{
    Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct);
}

// Source: https://huggingface.co/wonrax/phobert-base-vietnamese-sentiment
// Alt: https://huggingface.co/5CD-AI/Vietnamese-Sentiment-visobert
internal sealed class PhoBertSentimentAnalyzer : ISentimentAnalyzer
{
    public string Name => "sentiment-analysis";

    public Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct) =>
        throw new NotImplementedException("TODO: wire wonrax/phobert-base-vietnamese-sentiment via HF Inference.");
}
