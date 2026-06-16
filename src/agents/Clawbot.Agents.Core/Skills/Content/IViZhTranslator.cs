using System.Text.RegularExpressions;
using Clawbot.Agents.Core.Chat;

namespace Clawbot.Agents.Core.Skills.Content;

public sealed record TranslationResult(string Translated, string SourceLang, string TargetLang, IReadOnlyList<string> GlossaryHits);

public interface IViZhTranslator : ISkill
{
    Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct);
}

internal sealed partial class ClaudeViZhTranslator(IClaudeChatClient claude) : IViZhTranslator
{
    public string Name => "vi-zh-translation";

    public async Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var userMsg = $"Translate the following text from {sourceLang} to {targetLang}.\n\n" +
            $"Text:\n{text}\n\n" +
            "Return JSON: {\"translated\":\"...\",\"glossary_hits\":[\"term1\",\"term2\"]}";

        var reply = await claude.CompleteAsync(
            "You are a professional Vietnamese-Chinese translator for an education company. " +
            "Preserve technical terms, brand names, and proper nouns. " +
            "Track any specialized glossary terms you used. Return only valid JSON.",
            Array.Empty<ChatTurn>(),
            userMsg,
            ct).ConfigureAwait(false);

        return ParseResult(reply.Text, sourceLang, targetLang);
    }

    internal static TranslationResult ParseResult(string json, string src, string tgt)
    {
        var translated = ExtractField(json, "translated") ?? json.Trim();

        var hits = new List<string>();
        var arrayMatch = GlossaryArrayRegex().Match(json);
        if (arrayMatch.Success)
        {
            foreach (Match m in GlossaryItemRegex().Matches(arrayMatch.Groups[1].Value))
                hits.Add(m.Groups[1].Value.Trim());
        }

        return new TranslationResult(translated, src, tgt, hits);
    }

    private static string? ExtractField(string json, string key)
    {
        var idx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (idx < 0) return null;
        var afterKey = json[(idx + key.Length + 2)..];
        var m = JsonKeyValueRegex().Match(afterKey);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@":\s*""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonKeyValueRegex();

    [GeneratedRegex(@"""glossary_hits""\s*:\s*\[(.*?)\]", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex GlossaryArrayRegex();

    [GeneratedRegex(@"""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex GlossaryItemRegex();
}
