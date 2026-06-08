using System.Globalization;
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
    string? SourcePlatform = null);

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
    bool SpamFlagged = false);

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
    IOptions<ToxicityOptions> toxicityOptions)
{
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

    public async Task<ChatAgentReply> ReplyAsync(ChatAgentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = System.Diagnostics.Stopwatch.StartNew();

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

        // C3: Inbound spam detection — flag (no auto-reply block, but mark)
        var spamSignal = await _spam.EvaluateAsync(request.UserText, request.SenderHandle, request.SourcePlatform, ct).ConfigureAwait(false);

        var redacted = await _pii.RedactAsync(request.UserText, ct).ConfigureAwait(false);
        var intentResult = await _intent.ClassifyAsync(redacted.RedactedText, locale: null, ct).ConfigureAwait(false);

        // C1: Language detection → inject "reply in {lang}" directive
        var langResult = await _language.DetectAsync(redacted.RedactedText, ct).ConfigureAwait(false);

        var chunks = await _rag.RetrieveAsync(
            new RagRequest(request.TenantId, request.KbModuleCode, redacted.RedactedText, TopK: 4),
            ct).ConfigureAwait(false);

        var system = BuildSystemPrompt(chunks, intentResult.Label, langResult.LanguageCode);
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
            request.TenantId, "chat-agent", "claude",
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);

        started.Stop();
        return new ChatAgentReply(reply.Text, chunks,
            reply.InputTokens, reply.OutputTokens, reply.UsdCost, started.ElapsedMilliseconds,
            Intent: intentResult.Label, Blocked: false, BlockReason: null,
            Language: langResult.LanguageCode,
            ToxicityBlocked: false,
            SpamFlagged: spamSignal.IsSpam);
    }

    private static string BuildSystemPrompt(IReadOnlyList<RagChunk> chunks, string intent, string languageCode)
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
