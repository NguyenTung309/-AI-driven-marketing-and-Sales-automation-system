using System.Text.RegularExpressions;

namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record PiiSpan(string Type, int Start, int End, string Replacement);

public sealed record RedactionResult(string RedactedText, IReadOnlyList<PiiSpan> Spans);

public interface IPiiRedactor : ISkill
{
    Task<RedactionResult> RedactAsync(string text, CancellationToken ct);
}

// Baseline regex redactor for VN phone + email + 12-digit CCCD/ID.
// Vendor swap target: microsoft/presidio analyzer+anonymizer REST sidecar.
internal sealed partial class RegexPiiRedactor : IPiiRedactor
{
    [GeneratedRegex(@"(?<![\d])(0[35789]\d{8})(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<![\d])(\d{12})(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex IdCardRegex();

    public string Name => "pii-redaction";

    public Task<RedactionResult> RedactAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(new RedactionResult(text ?? string.Empty, Array.Empty<PiiSpan>()));

        var spans = new List<PiiSpan>();
        Collect(text, PhoneRegex(), "phone", "[PHONE]", spans);
        Collect(text, EmailRegex(), "email", "[EMAIL]", spans);
        Collect(text, IdCardRegex(), "id_card", "[ID]", spans);

        if (spans.Count == 0)
            return Task.FromResult(new RedactionResult(text, Array.Empty<PiiSpan>()));

        spans.Sort((a, b) => b.Start.CompareTo(a.Start));
        var buf = new System.Text.StringBuilder(text);
        foreach (var s in spans)
            buf.Remove(s.Start, s.End - s.Start).Insert(s.Start, s.Replacement);

        return Task.FromResult(new RedactionResult(buf.ToString(), spans));
    }

    private static void Collect(string text, Regex regex, string type, string replacement, List<PiiSpan> spans)
    {
        foreach (Match m in regex.Matches(text))
            spans.Add(new PiiSpan(type, m.Index, m.Index + m.Length, replacement));
    }
}
