namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record LanguageDetection(string LanguageCode, float Confidence);  // vi|en|zh|...

public interface ILanguageDetector : ISkill
{
    Task<LanguageDetection> DetectAsync(string text, CancellationToken ct);
}

// Source: https://fasttext.cc/docs/en/language-identification.html (lid.176)
internal sealed class FastTextLanguageDetector : ILanguageDetector
{
    public string Name => "language-detection";

    public Task<LanguageDetection> DetectAsync(string text, CancellationToken ct) =>
        throw new NotImplementedException("TODO: load fasttext lid.176 model or call REST sidecar.");
}
