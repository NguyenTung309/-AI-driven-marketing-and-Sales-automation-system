using System.Text;
using System.Text.RegularExpressions;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Ops;

namespace Clawbot.Agents.Core.Skills.Lead;

// LLM-backed lead-signal classifier. Understands context/synonyms a keyword list misses
// ("lớp này đông không ạ?" → asked_class_size). Falls back to the keyword baseline when the
// LLM returns nothing usable or errors, so scoring never silently stops.
// Source: Anthropic Claude via IClaudeChatClient (per-tenant config resolved by ScopedLlmChatClient).
public sealed partial class ClaudeLeadSignalClassifier(
    IClaudeChatClient claude,
    KeywordLeadSignalClassifier fallback,
    IClaudeCostTracker? costTracker = null,
    ILlmCallScope? llmScope = null) : ILeadSignalClassifier
{
    private readonly IClaudeChatClient _claude = claude;
    private readonly KeywordLeadSignalClassifier _fallback = fallback;
    private readonly IClaudeCostTracker? _costTracker = costTracker;
    private readonly ILlmCallScope? _llmScope = llmScope;

    private static readonly string SystemPrompt =
        "You label a single inbound customer message from a Chinese-language tutoring center's sales chat. " +
        "Decide which of these interest signals it expresses. Codes:\n" +
        "- asked_substantive_question: a real question showing interest (NOT a bare \"vâng/ok/để em xem\").\n" +
        "- asked_class_size: asks how many students per class / sĩ số.\n" +
        "- asked_schedule: asks about class times / lịch học.\n" +
        "- asked_teacher: asks about the teacher / giáo viên.\n" +
        "- asked_commitment: asks about output guarantee / cam kết đầu ra.\n" +
        "- asked_price: asks about tuition / học phí / giá.\n" +
        "- purchase_intent: wants to enroll/pay now / chốt / đăng ký luôn.\n" +
        "Return ONLY a JSON array of the matching codes, e.g. [\"asked_price\",\"asked_schedule\"]. " +
        "Return [] if none apply. No prose.";

    public string Name => "lead-signal-classification-llm";

    public async Task<LeadSignalResult> ClassifyAsync(string message, string? locale, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new LeadSignalResult(Array.Empty<string>());

        try
        {
            var reply = await _claude.CompleteAsync(
                SystemPrompt, Array.Empty<ChatTurn>(), message, ct).ConfigureAwait(false);
            await RecordCostAsync(reply, ct).ConfigureAwait(false);

            var codes = ParseCodes(reply.Text);
            // Empty LLM result on a non-trivial message is suspicious — cross-check with keywords.
            if (codes.Count == 0)
                return await _fallback.ClassifyAsync(message, locale, ct).ConfigureAwait(false);

            return new LeadSignalResult(codes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // LLM unavailable / unconfigured → degrade to keywords rather than dropping the signal.
            return await _fallback.ClassifyAsync(message, locale, ct).ConfigureAwait(false);
        }
    }

    internal static List<string> ParseCodes(string llmText)
    {
        var known = new HashSet<string>(LeadSignalCodes.All, StringComparer.Ordinal);
        var codes = new List<string>();
        foreach (Match m in TokenRegex().Matches(llmText ?? string.Empty))
        {
            var code = m.Groups[1].Value;
            if (known.Contains(code) && !codes.Contains(code, StringComparer.Ordinal))
                codes.Add(code);
        }
        return codes;
    }

    private async Task RecordCostAsync(ClaudeReply reply, CancellationToken ct)
    {
        var current = _llmScope?.Current;
        if (_costTracker is null || current is null || reply.UsdCost <= 0m)
            return;

        await _costTracker.RecordAsync(new CostEntry(
            current.Value.TenantId,
            current.Value.AgentCode,
            reply.Model,
            reply.InputTokens,
            reply.OutputTokens,
            reply.UsdCost,
            current.Value.CostAt ?? DateTimeOffset.UtcNow,
            current.Value.ReservationId), ct).ConfigureAwait(false);
    }

    // Matches quoted snake_case tokens like "asked_price" in the JSON array.
    [GeneratedRegex("\"([a-z_]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
