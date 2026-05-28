namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record PiiSpan(string Type, int Start, int End, string Replacement);

public sealed record RedactionResult(string RedactedText, IReadOnlyList<PiiSpan> Spans);

public interface IPiiRedactor : ISkill
{
    Task<RedactionResult> RedactAsync(string text, CancellationToken ct);
}

// Source: https://github.com/microsoft/presidio (REST sidecar — Analyzer + Anonymizer).
// Add VI recognizer for phone (e.g. 0[3|5|7|8|9]xxxxxxxx) + email.
internal sealed class PresidioPiiRedactor : IPiiRedactor
{
    public string Name => "pii-redaction";

    public Task<RedactionResult> RedactAsync(string text, CancellationToken ct) =>
        throw new NotImplementedException("TODO: call presidio analyzer + anonymizer endpoints via HttpClient.");
}
