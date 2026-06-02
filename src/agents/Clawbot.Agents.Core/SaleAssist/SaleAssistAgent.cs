using System.Globalization;
using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;

namespace Clawbot.Agents.Core.SaleAssist;

public sealed record ConversationContext(
    Guid TenantId,
    Guid ConversationId,
    string? ContactName,
    string Platform,
    IReadOnlyList<TurnSnapshot> RecentTurns);

public sealed record TurnSnapshot(string Direction, string Content, DateTimeOffset SentAt);

public sealed record DraftResult(
    string DraftText,
    string SuggestedAction,
    int LeadScoreHint,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs);

public sealed record SummaryResult(
    string Summary,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs);

public sealed class SaleAssistAgent(IRagRetriever rag, IClaudeChatClient claude)
{
    private const string DraftSystem =
        "You are ClawBot Sale Assist. Help the human sales rep by drafting the NEXT reply to send to the customer. " +
        "Keep it warm, concise (<=80 words), Vietnamese unless the customer is using Chinese. " +
        "Return ONLY the proposed reply text — no preamble.";

    private const string SummarySystem =
        "You are ClawBot Sale Assist. Summarize the conversation in 3 bullet points: customer goal, blockers, next best action. " +
        "Use Vietnamese. Keep each bullet under 20 words.";

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;

    public async Task<DraftResult> DraftAsync(ConversationContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var lastCustomerText = ctx.RecentTurns.LastOrDefault(t => t.Direction == "in")?.Content ?? string.Empty;
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(ctx.TenantId, KbModuleCode: null, lastCustomerText, TopK: 3),
            ct).ConfigureAwait(false);

        var history = ctx.RecentTurns
            .Select(t => new ChatTurn(t.Direction == "in" ? "user" : "assistant", t.Content))
            .ToList();

        var system = AppendKb(DraftSystem, chunks);
        var prompt = $"Customer last said: \"{lastCustomerText}\". Draft the next reply.";
        var reply = await _claude.CompleteAsync(system, history, prompt, ct).ConfigureAwait(false);

        var action = InferAction(reply.Text, ctx.RecentTurns);
        var scoreHint = HintLeadScore(ctx.RecentTurns);

        sw.Stop();
        return new DraftResult(reply.Text.Trim(), action, scoreHint,
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, sw.ElapsedMilliseconds);
    }

    public async Task<SummaryResult> SummarizeAsync(ConversationContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var transcript = BuildTranscript(ctx.RecentTurns);
        var reply = await _claude.CompleteAsync(SummarySystem,
            history: Array.Empty<ChatTurn>(),
            userMessage: $"Transcript:\n{transcript}\n\nSummary:",
            ct).ConfigureAwait(false);

        sw.Stop();
        return new SummaryResult(reply.Text.Trim(),
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, sw.ElapsedMilliseconds);
    }

    private static string AppendKb(string baseSystem, IReadOnlyList<RagChunk> chunks)
    {
        if (chunks.Count == 0) return baseSystem;
        var sb = new StringBuilder(baseSystem.Length + 256);
        sb.AppendLine(baseSystem);
        sb.AppendLine();
        sb.AppendLine("## Knowledge base hints:");
        for (var i = 0; i < chunks.Count; i++)
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] (module={chunks[i].KbModuleCode}) {chunks[i].Snippet}");
        return sb.ToString();
    }

    private static string BuildTranscript(IReadOnlyList<TurnSnapshot> turns)
    {
        var sb = new StringBuilder();
        foreach (var t in turns)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{(t.Direction == "in" ? "Customer" : "Agent")}: {t.Content}");
        return sb.ToString();
    }

    private static string InferAction(string draftText, IReadOnlyList<TurnSnapshot> turns)
    {
        var lower = draftText.ToUpperInvariant();
        if (lower.Contains("LICH HOC", StringComparison.Ordinal) || lower.Contains("BOOK", StringComparison.Ordinal))
            return "book_trial";
        if (lower.Contains("PRICE", StringComparison.Ordinal) || lower.Contains("HOC PHI", StringComparison.Ordinal))
            return "send_quote";
        if (turns.Count < 3) return "ask_goal";
        return "follow_up";
    }

    private static int HintLeadScore(IReadOnlyList<TurnSnapshot> turns)
    {
        var inCount = turns.Count(t => t.Direction == "in");
        if (inCount >= 5) return 70;
        if (inCount >= 3) return 50;
        if (inCount >= 1) return 30;
        return 10;
    }
}
