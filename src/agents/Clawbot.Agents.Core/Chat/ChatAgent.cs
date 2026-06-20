using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Chat;

public sealed record ChatAgentRequest(
    Guid TenantId,
    Guid? ConversationId,
    string? KbModuleCode,
    string UserText,
    IReadOnlyList<ChatTurn> History,
    string? SenderHandle = null,
    string? SourcePlatform = null,
    string? MatchedScenarioTemplate = null);

public sealed record ChatAgentReply(
    string Text,
    IReadOnlyList<RagChunk> Citations,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs,
    string Intent,
    bool Blocked,
    string? BlockReason,
    string? Language = null,
    bool ToxicityBlocked = false,
    bool SpamFlagged = false,
    bool Escalate = false);

public sealed record ChatAgentStreamChunk(string Text, bool Final, ChatAgentReply? Reply = null);

public sealed class ChatAgent(
    IRagRetriever rag,
    IClaudeChatClient claude,
    IIntentClassifier intent,
    IPiiRedactor pii,
    IPromptInjectionDefender injection,
    IClaudeCostTracker cost,
    ILanguageDetector language,
    IToxicityFilter toxicity,
    ISpamDetector spam,
    IOptions<ToxicityOptions> toxicityOptions,
    IAgentToggleGate toggle,
    ILlmCallScope llmScope)
{
    private const string AgentCode = "chat-agent";

    private const string DefaultSystemPrompt =
        "You are ClawBot — an omnichannel sales assistant for a Chinese-language tutoring center. " +
        "Answer concisely in the customer's language (default Vietnamese, switch to Chinese if asked). " +
        "Cite knowledge-base snippets when used. If unsure, say so and offer to escalate to a human sales rep.";

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;
    private readonly IIntentClassifier _intent = intent;
    private readonly IPiiRedactor _pii = pii;
    private readonly IPromptInjectionDefender _injection = injection;
    private readonly IClaudeCostTracker _cost = cost;
    private readonly ILanguageDetector _language = language;
    private readonly IToxicityFilter _toxicity = toxicity;
    private readonly ISpamDetector _spam = spam;
    private readonly ToxicityOptions _toxicityOptions = toxicityOptions.Value;
    private readonly IAgentToggleGate _toggle = toggle;
    private readonly ILlmCallScope _llmScope = llmScope;

    public async Task<ChatAgentReply> ReplyAsync(ChatAgentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var _llm = _llmScope.Begin(request.TenantId, AgentCode);
        var started = System.Diagnostics.Stopwatch.StartNew();

        // M25: skip auto-reply if the chat agent is disabled for this tenant.
        if (!await _toggle.IsAutoActionEnabledAsync(request.TenantId, "chat", ct).ConfigureAwait(false))
        {
            started.Stop();
            return new ChatAgentReply(
                string.Empty, Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "disabled", Blocked: true, BlockReason: "agent_disabled");
        }

        var verdict = await _injection.InspectAsync(request.UserText, ct).ConfigureAwait(false);
        if (verdict.IsMalicious)
        {
            started.Stop();
            return new ChatAgentReply(
                "Tôi không thể xử lý yêu cầu này. Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "blocked", Blocked: true, BlockReason: string.Join("; ", verdict.Reasons));
        }

        // C2: Inbound toxicity check — block if above configurable threshold
        var inboundToxic = await _toxicity.IsBlockedAsync(request.UserText, _toxicityOptions.InboundBlockThreshold, ct).ConfigureAwait(false);
        if (inboundToxic)
        {
            started.Stop();
            return new ChatAgentReply(
                "Tin nhắn của bạn chứa nội dung không phù hợp. Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "toxic_blocked", Blocked: true, BlockReason: "toxicity",
                ToxicityBlocked: true);
        }

        var costSummary = await _cost.SummaryAsync(request.TenantId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (IsCostCapReached(costSummary))
        {
            started.Stop();
            return new ChatAgentReply(
                "Đã đạt hạn mức chi phí AI tháng này. Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "cost_cap", Blocked: true, BlockReason: "cost_cap_exceeded");
        }

        // C3: Inbound spam detection — flag (no auto-reply block, but mark)
        var spamSignal = await _spam.EvaluateAsync(request.UserText, request.SenderHandle, request.SourcePlatform, ct).ConfigureAwait(false);

        var redacted = await _pii.RedactAsync(request.UserText, ct).ConfigureAwait(false);
        var intentResult = await _intent.ClassifyAsync(redacted.RedactedText, locale: null, ct).ConfigureAwait(false);

        // C1: Language detection → inject "reply in {lang}" directive
        var langResult = await _language.DetectAsync(redacted.RedactedText, ct).ConfigureAwait(false);

        var chunks = await _rag.RetrieveAsync(
            new RagRequest(request.TenantId, request.KbModuleCode, redacted.RedactedText, TopK: 4),
            ct).ConfigureAwait(false);

        var system = BuildSystemPrompt(chunks, intentResult.Label, langResult.LanguageCode, request.MatchedScenarioTemplate);
        var reply = await _claude.CompleteAsync(system, request.History, redacted.RedactedText, ct).ConfigureAwait(false);

        // C2: Outbound toxicity scan — block/regenerate if Claude output is toxic
        var outboundToxic = await _toxicity.IsBlockedAsync(reply.Text, _toxicityOptions.OutboundBlockThreshold, ct).ConfigureAwait(false);
        if (outboundToxic)
        {
            started.Stop();
            return new ChatAgentReply(
                "Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), reply.InputTokens, reply.OutputTokens, reply.UsdCost,
                started.ElapsedMilliseconds, Intent: intentResult.Label,
                Blocked: true, BlockReason: "outbound_toxicity",
                Language: langResult.LanguageCode, ToxicityBlocked: true);
        }

        await _cost.RecordAsync(new CostEntry(
            request.TenantId, AgentCode, reply.Model,
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        var escalate = string.Equals(intentResult.Label, "escalation", StringComparison.OrdinalIgnoreCase)
            || chunks.Count == 0
            || (chunks.Count > 0 && chunks.Max(c => c.Score) < 0.35f);

        started.Stop();
        return new ChatAgentReply(reply.Text, chunks,
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, started.ElapsedMilliseconds,
            Intent: intentResult.Label, Blocked: false, BlockReason: null,
            Language: langResult.LanguageCode,
            ToxicityBlocked: false,
            SpamFlagged: spamSignal.IsSpam,
            Escalate: escalate);
    }

    public async IAsyncEnumerable<ChatAgentStreamChunk> StreamReplyAsync(
        ChatAgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var _llm = _llmScope.Begin(request.TenantId, AgentCode);
        var started = System.Diagnostics.Stopwatch.StartNew();

        if (!await _toggle.IsAutoActionEnabledAsync(request.TenantId, "chat", ct).ConfigureAwait(false))
        {
            started.Stop();
            yield return Final(new ChatAgentReply(
                string.Empty, Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "disabled", Blocked: true, BlockReason: "agent_disabled"));
            yield break;
        }

        var verdict = await _injection.InspectAsync(request.UserText, ct).ConfigureAwait(false);
        if (verdict.IsMalicious)
        {
            started.Stop();
            yield return Final(new ChatAgentReply(
                "Tôi không thể xử lý yêu cầu này. Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "blocked", Blocked: true, BlockReason: string.Join("; ", verdict.Reasons)));
            yield break;
        }

        var inboundToxic = await _toxicity.IsBlockedAsync(request.UserText, _toxicityOptions.InboundBlockThreshold, ct).ConfigureAwait(false);
        if (inboundToxic)
        {
            started.Stop();
            yield return Final(new ChatAgentReply(
                "Tin nhắn của bạn chứa nội dung không phù hợp. Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "toxic_blocked", Blocked: true, BlockReason: "toxicity",
                ToxicityBlocked: true));
            yield break;
        }

        var costSummary = await _cost.SummaryAsync(request.TenantId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (IsCostCapReached(costSummary))
        {
            started.Stop();
            yield return Final(new ChatAgentReply(
                "Đã đạt hạn mức chi phí AI tháng này. Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), 0, 0, 0m, started.ElapsedMilliseconds,
                Intent: "cost_cap", Blocked: true, BlockReason: "cost_cap_exceeded"));
            yield break;
        }

        var spamSignal = await _spam.EvaluateAsync(request.UserText, request.SenderHandle, request.SourcePlatform, ct).ConfigureAwait(false);
        var redacted = await _pii.RedactAsync(request.UserText, ct).ConfigureAwait(false);
        var intentResult = await _intent.ClassifyAsync(redacted.RedactedText, locale: null, ct).ConfigureAwait(false);
        var langResult = await _language.DetectAsync(redacted.RedactedText, ct).ConfigureAwait(false);
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(request.TenantId, request.KbModuleCode, redacted.RedactedText, TopK: 4),
            ct).ConfigureAwait(false);

        var system = BuildSystemPrompt(chunks, intentResult.Label, langResult.LanguageCode, request.MatchedScenarioTemplate);
        var text = new StringBuilder();
        var inputTokens = 0;
        var outputTokens = 0;
        var usdCost = 0m;
        var model = string.Empty;

        await foreach (var chunk in _claude.StreamAsync(system, request.History, redacted.RedactedText, ct).ConfigureAwait(false))
        {
            if (chunk.Final)
            {
                inputTokens = chunk.InputTokens;
                outputTokens = chunk.OutputTokens;
                usdCost = chunk.UsdCost;
                model = chunk.Model;
                continue;
            }

            text.Append(chunk.Text);
            yield return new ChatAgentStreamChunk(chunk.Text, Final: false);
        }

        var replyText = text.ToString();
        var outboundToxic = await _toxicity.IsBlockedAsync(replyText, _toxicityOptions.OutboundBlockThreshold, ct).ConfigureAwait(false);
        if (outboundToxic)
        {
            started.Stop();
            yield return Final(new ChatAgentReply(
                "Đang chuyển tới nhân viên hỗ trợ.",
                Array.Empty<RagChunk>(), inputTokens, outputTokens, usdCost,
                started.ElapsedMilliseconds, Intent: intentResult.Label,
                Blocked: true, BlockReason: "outbound_toxicity",
                Language: langResult.LanguageCode, ToxicityBlocked: true));
            yield break;
        }

        await _cost.RecordAsync(new CostEntry(
            request.TenantId, AgentCode, model,
            inputTokens, outputTokens, usdCost, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        var escalate = string.Equals(intentResult.Label, "escalation", StringComparison.OrdinalIgnoreCase)
            || chunks.Count == 0
            || (chunks.Count > 0 && chunks.Max(c => c.Score) < 0.35f);

        started.Stop();
        yield return Final(new ChatAgentReply(replyText, chunks,
            inputTokens, outputTokens, usdCost, started.ElapsedMilliseconds,
            Intent: intentResult.Label, Blocked: false, BlockReason: null,
            Language: langResult.LanguageCode,
            ToxicityBlocked: false,
            SpamFlagged: spamSignal.IsSpam,
            Escalate: escalate), text: string.Empty);
    }

    private static ChatAgentStreamChunk Final(ChatAgentReply reply, string? text = null) =>
        new(text ?? reply.Text, Final: true, reply);

    private static bool IsCostCapReached(CostSummary? summary) =>
        summary is { CapUsd: > 0m } && summary.MonthToDateUsd >= summary.CapUsd;

    private static string BuildSystemPrompt(
        IReadOnlyList<RagChunk> chunks,
        string intent,
        string languageCode,
        string? matchedScenarioTemplate)
    {
        var sb = new StringBuilder(DefaultSystemPrompt.Length + 256);
        sb.AppendLine(DefaultSystemPrompt);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Detected intent: {intent}.");

        // C1: Language directive — tell Claude to reply in detected language
        if (!string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(languageCode, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            var langName = languageCode switch
            {
                "vi" => "Vietnamese",
                "zh" => "Chinese",
                "ja" => "Japanese",
                "ko" => "Korean",
                "th" => "Thai",
                _ => languageCode
            };
            sb.AppendLine(CultureInfo.InvariantCulture, $"Reply in {langName}.");
        }

        if (!string.IsNullOrWhiteSpace(matchedScenarioTemplate))
        {
            sb.AppendLine();
            sb.AppendLine("## Matched chat scenario template");
            sb.AppendLine(matchedScenarioTemplate.Trim());
        }

        if (chunks.Count == 0) return sb.ToString();

        sb.AppendLine();
        sb.AppendLine("## Knowledge base snippets (cite by [#index] when used):");
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] (module={c.KbModuleCode}, score={c.Score:0.00}) {c.Snippet}");
        }
        return sb.ToString();
    }
}
