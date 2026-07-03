using System.Globalization;
using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.Extensions.Options;

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
    long LatencyMs,
    bool ToxicityBlocked = false);

public sealed record SummaryResult(
    string Summary,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs);

public sealed record UpsellResult(
    string Suggestion,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs);

public sealed class SaleAssistAgent(
    IRagRetriever rag,
    IClaudeChatClient claude,
    IConversationSummarizer summarizer,
    IPiiRedactor pii,
    IToxicityFilter toxicity,
    IOptions<ToxicityOptions> toxicityOptions,
    ILlmCallScope llmScope,
    ILlmCostTracker? costTracker = null)
{
    private const string AgentCode = "sale-assist";

    private const string DraftSystem =
        "You are ClawBot Sale Assist. Help the human sales rep by drafting the NEXT reply to send to the customer. " +
        "Keep it warm, concise (<=80 words), Vietnamese unless the customer is using Chinese. " +
        "Return ONLY the proposed reply text — no preamble.";

    private const string SummarySystem =
        "You are ClawBot Sale Assist. Summarize the conversation in 3 bullet points: customer goal, blockers, next best action. " +
        "Use Vietnamese. Keep each bullet under 20 words.";

    private const string UpsellSystem =
        "You are ClawBot Sale Assist. This customer is a hot lead near closing. Based ONLY on what they discussed, " +
        "propose ONE concrete upsell or cross-sell offer (combo, premium package, add-on, longer course) that fits their stated goal. " +
        "Vietnamese, <=60 words, friendly and specific. If the conversation shows no real closing signal, reply exactly 'NONE'. " +
        "Return ONLY the suggestion text.";

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;
    private readonly IConversationSummarizer _summarizer = summarizer;
    private readonly IPiiRedactor _pii = pii;
    private readonly IToxicityFilter _toxicity = toxicity;
    private readonly ToxicityOptions _toxicityOptions = toxicityOptions.Value;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly ILlmCostTracker? _costTracker = costTracker;

    public async Task<DraftResult> DraftAsync(ConversationContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        using var _llm = _llmScope.Begin(ctx.TenantId, AgentCode);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var redactedTurns = await RedactTurnsAsync(ctx.RecentTurns, ct).ConfigureAwait(false);
        var lastCustomerText = redactedTurns.LastOrDefault(t => t.Direction == "in")?.Content ?? string.Empty;
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(ctx.TenantId, KbModuleCode: null, lastCustomerText, TopK: 3),
            ct).ConfigureAwait(false);

        var history = redactedTurns
            .Select(t => new ChatTurn(t.Direction == "in" ? "user" : "assistant", t.Content))
            .ToList();

        var system = AppendKb(DraftSystem, chunks);
        var prompt = $"Customer last said: \"{lastCustomerText}\". Draft the next reply.";
        var reply = await _claude.CompleteAsync(system, history, prompt, ct).ConfigureAwait(false);
        await RecordCostAsync(ctx.TenantId, reply, ct).ConfigureAwait(false);

        // Tone check: block draft if toxic before showing to sale rep
        var isToxic = await _toxicity.IsBlockedAsync(reply.Text, _toxicityOptions.DraftBlockThreshold, ct).ConfigureAwait(false);
        if (isToxic)
        {
            sw.Stop();
            return new DraftResult(
                "[Draft blocked — toxic content detected. Escalate to manager.]",
                "escalate", HintLeadScore(ctx.RecentTurns),
                reply.InputTokens, reply.OutputTokens, reply.UsdCost, sw.ElapsedMilliseconds,
                ToxicityBlocked: true);
        }

        var action = InferAction(reply.Text, ctx.RecentTurns);
        var scoreHint = HintLeadScore(ctx.RecentTurns);

        sw.Stop();
        return new DraftResult(reply.Text.Trim(), action, scoreHint,
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, sw.ElapsedMilliseconds);
    }

    public async Task<SummaryResult> SummarizeAsync(ConversationContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        using var _llm = _llmScope.Begin(ctx.TenantId, AgentCode);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var redactedTurns = await RedactTurnsAsync(ctx.RecentTurns, ct).ConfigureAwait(false);
        var transcript = BuildTranscript(redactedTurns);
        var reply = await _claude.CompleteAsync(SummarySystem,
            history: Array.Empty<ChatTurn>(),
            userMessage: $"Transcript:\n{transcript}\n\nSummary:",
            ct).ConfigureAwait(false);
        await RecordCostAsync(ctx.TenantId, reply, ct).ConfigureAwait(false);

        sw.Stop();
        return new SummaryResult(reply.Text.Trim(),
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, sw.ElapsedMilliseconds);
    }

    // SaleAssist-4: contextual upsell suggestion. Caller gates on lead.Stage=='hot' before invoking;
    // Claude reads the recent turns + KB hints and proposes a concrete offer (or 'NONE' if no closing signal).
    public async Task<UpsellResult> SuggestUpsellAsync(ConversationContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        using var _llm = _llmScope.Begin(ctx.TenantId, AgentCode);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var redactedTurns = await RedactTurnsAsync(ctx.RecentTurns, ct).ConfigureAwait(false);
        var lastCustomerText = redactedTurns.LastOrDefault(t => t.Direction == "in")?.Content ?? string.Empty;
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(ctx.TenantId, KbModuleCode: null, lastCustomerText, TopK: 3), ct).ConfigureAwait(false);

        var system = AppendKb(UpsellSystem, chunks);
        var transcript = BuildTranscript(redactedTurns);
        var reply = await _claude.CompleteAsync(
            system, history: Array.Empty<ChatTurn>(),
            userMessage: $"Conversation so far:\n{transcript}\n\nUpsell suggestion:", ct).ConfigureAwait(false);
        await RecordCostAsync(ctx.TenantId, reply, ct).ConfigureAwait(false);

        sw.Stop();
        return new UpsellResult(reply.Text.Trim(),
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, sw.ElapsedMilliseconds);
    }

    // Auto-summary for Resolve — uses IConversationSummarizer (Claude) + persists to agent_sessions trace.
    public async Task<Core.Skills.Nlp.SummaryResult> AutoSummaryAsync(ConversationContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        using var _llm = _llmScope.Begin(ctx.TenantId, AgentCode);

        var redactedTurns = await RedactTurnsAsync(ctx.RecentTurns, ct).ConfigureAwait(false);
        var turns = redactedTurns
            .Select(t => new ConversationTurn(
                t.Direction == "in" ? "customer" : "agent",
                t.Content,
                t.SentAt))
            .ToList();

        return await _summarizer.SummarizeAsync(turns, maxWords: 100, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TurnSnapshot>> RedactTurnsAsync(IReadOnlyList<TurnSnapshot> turns, CancellationToken ct)
    {
        if (turns.Count == 0) return Array.Empty<TurnSnapshot>();
        var redacted = new List<TurnSnapshot>(turns.Count);
        foreach (var turn in turns)
        {
            var content = await _pii.RedactAsync(turn.Content, ct).ConfigureAwait(false);
            redacted.Add(new TurnSnapshot(turn.Direction, content.RedactedText, turn.SentAt));
        }
        return redacted;
    }

    private async Task RecordCostAsync(Guid tenantId, ClaudeReply reply, CancellationToken ct)
    {
        if (_costTracker is null || reply.UsdCost <= 0m)
            return;

        await _costTracker.RecordAsync(new CostEntry(
            tenantId,
            AgentCode,
            reply.Model,
            reply.InputTokens,
            reply.OutputTokens,
            reply.UsdCost,
            _llmScope.Current?.CostAt ?? DateTimeOffset.UtcNow,
            _llmScope.Current?.ReservationId), ct).ConfigureAwait(false);
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
