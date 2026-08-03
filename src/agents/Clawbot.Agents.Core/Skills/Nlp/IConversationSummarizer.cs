using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record ConversationTurn(string Role, string Content, DateTimeOffset At);

public sealed record SummaryResult(string Summary, IReadOnlyList<string> KeyPoints);

public interface IConversationSummarizer : ISkill
{
    Task<SummaryResult> SummarizeAsync(IReadOnlyList<ConversationTurn> turns, int maxWords, CancellationToken ct);
}

public sealed partial class SummarizerOptions
{
    public const string SectionName = "Skills:Summarizer";
    public string PromptTemplate { get; set; } =
        "Summarize the following conversation in at most {maxWords} words. " +
        "Return JSON: {\"summary\":\"...\",\"key_points\":[\"...\"]}. " +
        "Use the same language as the conversation.\n\n{turns}";
}

// Source: Anthropic Claude Sonnet 4.6 via IClaudeChatClient.
internal sealed partial class ClaudeConversationSummarizer : IConversationSummarizer
{
    private readonly IClaudeChatClient _claude;
    private readonly SummarizerOptions _options;
    private readonly ILlmCostTracker? _costTracker;
    private readonly ILlmCallScope? _llmScope;

    public ClaudeConversationSummarizer(
        IClaudeChatClient claude,
        IOptions<SummarizerOptions> options,
        ILlmCostTracker? costTracker = null,
        ILlmCallScope? llmScope = null)
    {
        _claude = claude;
        _options = options.Value;
        _costTracker = costTracker;
        _llmScope = llmScope;
    }

    public string Name => "conversation-summarization";

    public async Task<SummaryResult> SummarizeAsync(IReadOnlyList<ConversationTurn> turns, int maxWords, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(turns);
        if (turns.Count == 0)
            return new SummaryResult(string.Empty, Array.Empty<string>());

        var transcript = BuildTranscript(turns);
        var prompt = _options.PromptTemplate
            .Replace("{maxWords}", maxWords.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{turns}", transcript, StringComparison.Ordinal);

        var reply = await _claude.CompleteAsync(
            "You are a conversation summarizer. Return only valid JSON.",
            Array.Empty<ChatTurn>(),
            prompt,
            ct).ConfigureAwait(false);
        await RecordCostAsync(reply, ct).ConfigureAwait(false);

        return ParseSummary(reply.Text);
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

    private static string BuildTranscript(IReadOnlyList<ConversationTurn> turns)
    {
        var sb = new StringBuilder(turns.Count * 120);
        foreach (var t in turns)
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{t.At:yyyy-MM-dd HH:mm}] {t.Role}: {t.Content}");
        return sb.ToString();
    }

    internal static SummaryResult ParseSummary(string json)
    {
        var summaryMatch = SummaryRegex().Match(json);
        var summary = summaryMatch.Success ? summaryMatch.Groups[1].Value.Trim() : json.Trim();

        var keyPoints = new List<string>();
        var arrayMatch = KeyPointsArrayRegex().Match(json);
        if (arrayMatch.Success)
        {
            foreach (Match m in KeyPointItemRegex().Matches(arrayMatch.Groups[1].Value))
                keyPoints.Add(m.Groups[1].Value.Trim());
        }

        return new SummaryResult(summary, keyPoints);
    }

    [GeneratedRegex(@"""summary""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryRegex();

    [GeneratedRegex(@"""key_points""\s*:\s*\[(.*?)\]", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex KeyPointsArrayRegex();

    [GeneratedRegex(@"""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPointItemRegex();
}
