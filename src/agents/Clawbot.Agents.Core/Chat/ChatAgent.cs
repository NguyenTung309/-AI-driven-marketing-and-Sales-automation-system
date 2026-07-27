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
    string? MatchedScenarioTemplate = null,
    // Prompt custom cua tenant (config.SystemPrompt). Rong -> dung DefaultSystemPrompt. Luon boc guardrail.
    string? CustomSystemPrompt = null,
    // ai-self-learning-memory Lop 2: top-k facts AI nho ve khach (da redact), caller load tu contact_memories.
    IReadOnlyList<string>? ContactFacts = null);

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
    ILlmCostTracker cost,
    ILanguageDetector language,
    IToxicityFilter toxicity,
    ISpamDetector spam,
    IOptions<ToxicityOptions> toxicityOptions,
    IAgentToggleGate toggle,
    ILlmCallScope llmScope)
{
    private const string AgentCode = "chat-agent";

    private const string DefaultSystemPrompt =
        "Bạn là tư vấn viên của trung tâm dạy tiếng Trung, đang chat với khách qua Zalo/Facebook. " +
        "Tư vấn khóa học, lộ trình, học phí; thân thiện, chủ động hỏi nhu cầu và mời để lại số điện thoại " +
        "hoặc đặt lịch học thử khi phù hợp. Không chắc thì nói sẽ nhờ nhân viên hỗ trợ.";

    // Văn phong khoá cho hot-path chat (không nằm trong BaseGuardrail vì guardrail dùng chung cho mọi
    // agent — reviewer/orchestrator không cần giọng chat). Trị đúng bệnh reply "sượng": lộ meta
    // "dựa trên tài liệu", liệt kê những gì thiếu, bullet máy móc, dồn nhiều CTA.
    private const string ChatToneRules =
        "# Văn phong bắt buộc khi nhắn với khách\n" +
        "- Nhắn như một tư vấn viên thật đang chat: câu ngắn, ấm, tự nhiên.\n" +
        "- Xưng hô theo ngữ cảnh, đối đúng cặp khách đã thiết lập và giữ nhất quán cả hội thoại: " +
        "chưa rõ thì xưng \"mình\" gọi \"bạn\"; khách xưng \"em\" → mình xưng \"chị\", gọi khách \"em\"; " +
        "khách xưng \"anh/chị\" → mình xưng \"em\"; khách xưng \"chú/cô/bác\" → mình xưng \"cháu\" và gọi đúng vai đó.\n" +
        "- TUYỆT ĐỐI không nhắc tới \"tài liệu\", \"kho tri thức\", \"dữ liệu\", \"hệ thống\", \"thông tin được cung cấp\" — trả lời như thể mình tự biết.\n" +
        "- Không kể ra những gì mình KHÔNG biết hay chưa có. Thiếu thông tin nào thì bỏ qua, hoặc nói tự nhiên: \"phần này để mình kiểm tra lại rồi báo bạn ngay nhé\".\n" +
        "- Không mở đầu bằng \"Dựa trên...\", \"Theo thông tin...\" — vào thẳng nội dung.\n" +
        "- Hạn chế gạch đầu dòng: chỉ dùng khi khách hỏi chi tiết nhiều mục (bảng giá, lịch học); còn lại viết thành câu chat bình thường.\n" +
        "- Kết thúc bằng tối đa MỘT câu hỏi hoặc một lời mời duy nhất, theo mạch chuyện — không dồn nhiều lựa chọn cùng lúc.";

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;
    private readonly IIntentClassifier _intent = intent;
    private readonly IPiiRedactor _pii = pii;
    private readonly IPromptInjectionDefender _injection = injection;
    private readonly ILlmCostTracker _cost = cost;
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

        var redactedHistory = await RedactHistoryAsync(request.History, ct).ConfigureAwait(false);
        var system = BuildSystemPrompt(chunks, intentResult.Label, langResult.LanguageCode, request.MatchedScenarioTemplate, request.CustomSystemPrompt, request.ContactFacts);
        var reply = await _claude.CompleteAsync(system, redactedHistory, redacted.RedactedText, ct).ConfigureAwait(false);

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
            reply.InputTokens, reply.OutputTokens, reply.UsdCost,
            _llmScope.Current?.CostAt ?? DateTimeOffset.UtcNow,
            _llmScope.Current?.ReservationId,
            SessionId: null,
            IsEstimated: reply.IsEstimated), ct).ConfigureAwait(false);

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

        var redactedHistory = await RedactHistoryAsync(request.History, ct).ConfigureAwait(false);
        var system = BuildSystemPrompt(chunks, intentResult.Label, langResult.LanguageCode, request.MatchedScenarioTemplate, request.CustomSystemPrompt, request.ContactFacts);
        var text = new StringBuilder();
        var inputTokens = 0;
        var outputTokens = 0;
        var usdCost = 0m;
        var model = string.Empty;
        var isEstimated = false;

        await foreach (var chunk in _claude.StreamAsync(system, redactedHistory, redacted.RedactedText, ct).ConfigureAwait(false))
        {
            if (chunk.Final)
            {
                inputTokens = chunk.InputTokens;
                outputTokens = chunk.OutputTokens;
                usdCost = chunk.UsdCost;
                model = chunk.Model;
                isEstimated = chunk.IsEstimated;
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
            inputTokens, outputTokens, usdCost,
            _llmScope.Current?.CostAt ?? DateTimeOffset.UtcNow,
            _llmScope.Current?.ReservationId,
            SessionId: null,
            IsEstimated: isEstimated), ct).ConfigureAwait(false);

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

    private async Task<IReadOnlyList<ChatTurn>> RedactHistoryAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        if (history.Count == 0) return Array.Empty<ChatTurn>();
        var redacted = new List<ChatTurn>(history.Count);
        foreach (var turn in history)
        {
            var content = await _pii.RedactAsync(turn.Content, ct).ConfigureAwait(false);
            redacted.Add(new ChatTurn(turn.Role, content.RedactedText));
        }
        return redacted;
    }

    private static ChatAgentStreamChunk Final(ChatAgentReply reply, string? text = null) =>
        new(text ?? reply.Text, Final: true, reply);

    private static bool IsCostCapReached(CostSummary? summary) =>
        summary is { CapUsd: > 0m } && summary.MonthToDateUsd >= summary.CapUsd;

    private static string BuildSystemPrompt(
        IReadOnlyList<RagChunk> chunks,
        string intent,
        string languageCode,
        string? matchedScenarioTemplate,
        string? customSystemPrompt = null,
        IReadOnlyList<string>? contactFacts = null)
    {
        // Guardrail (khoa) + persona: custom cua tenant neu co, khong thi mau mac dinh.
        var persona = string.IsNullOrWhiteSpace(customSystemPrompt) ? DefaultSystemPrompt : customSystemPrompt;
        var sb = new StringBuilder(persona.Length + 1024);
        sb.AppendLine(AgentPromptDefaults.Compose(persona));
        // Văn phong khoá: đặt SAU persona để tenant không vô tình ghi đè bằng "Hướng dẫn trả lời".
        sb.AppendLine();
        sb.AppendLine(ChatToneRules);
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

        // ai-self-learning-memory Lop 2: tri nho dai han ve khach — dung tu nhien, khong doc lai
        // nguyen van kieu "toi nho ban la..."; fact co the cu, uu tien thong tin khach vua noi.
        if (contactFacts is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("## Ghi nho ve khach hang nay (tham khao, khach vua noi khac thi theo khach):");
            foreach (var fact in contactFacts)
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {fact}");
        }

        if (chunks.Count == 0) return sb.ToString();

        sb.AppendLine();
        // Không ép "cite [#index]" trong reply: từng khiến model mở đầu "Dựa trên tài liệu..." với khách.
        // Citations trace theo chunks retrieve được, không parse từ text.
        sb.AppendLine("## Thông tin nội bộ để trả lời (không nhắc tới nguồn/tài liệu trong tin nhắn):");
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] (module={c.KbModuleCode}, score={c.Score:0.00}) {c.Snippet}");
        }
        return sb.ToString();
    }
}
