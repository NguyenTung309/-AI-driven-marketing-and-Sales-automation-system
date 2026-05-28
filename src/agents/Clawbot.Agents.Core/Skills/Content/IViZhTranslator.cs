namespace Clawbot.Agents.Core.Skills.Content;

public sealed record TranslationResult(string Translated, string SourceLang, string TargetLang, IReadOnlyList<string> GlossaryHits);

public interface IViZhTranslator : ISkill
{
    Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct);
}

// Source: Claude Sonnet 4.6 + glossary stored as KB module (`kb_modules.code = 'GLOSSARY-VI-ZH'`).
internal sealed class ClaudeViZhTranslator : IViZhTranslator
{
    public string Name => "vi-zh-translation";

    public Task<TranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken ct) =>
        throw new NotImplementedException("TODO: load glossary KB version + Claude prompt with terminology constraints.");
}
