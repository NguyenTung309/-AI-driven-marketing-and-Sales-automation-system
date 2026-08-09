using Clawbot.Agents.Contracts.Chat;
using Clawbot.Domain.Agents;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreChat = Clawbot.Agents.Core.Chat;

namespace Clawbot.AgentService.Services;

public sealed partial class ChatAgentGrpcService(
    CoreChat.ChatAgent agent,
    Clawbot.Agents.Core.Content.ContentReviewer reviewer,
    AppDbContext db,
    IClock clock,
    LeadAutoScorer leadScorer,
    ILogger<ChatAgentGrpcService> logger,
    IChannelAdapter? channelAdapter = null,
    Clawbot.SharedKernel.Inbox.IChatApprovalPolicyResolver? approvalPolicy = null) : ChatAgent.ChatAgentBase
{
    // Review-gate P2: cap cho LLM critic trên hot-path chat (consumer tuần tự) — quá hạn coi như needs_human.
    private static readonly TimeSpan ChatReviewTimeout = TimeSpan.FromSeconds(30);
    // Hot-path auto-reply: bao toàn bộ stream + review + send. Quá hạn phải Fail session (không để running vĩnh viễn).
    // Khớp gần deadline gRPC client (GrpcChatAutoReplyGateway ~100s) để consumer không treo.
    private static readonly TimeSpan ChatReplyTimeout = TimeSpan.FromSeconds(90);

    private readonly CoreChat.ChatAgent _agent = agent;
    private readonly Clawbot.Agents.Core.Content.ContentReviewer _reviewer = reviewer;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly LeadAutoScorer _leadScorer = leadScorer;
    private readonly IChannelAdapter? _channelAdapter = channelAdapter;
    private readonly Clawbot.SharedKernel.Inbox.IChatApprovalPolicyResolver? _approvalPolicy = approvalPolicy;
    private readonly ILogger<ChatAgentGrpcService> _logger = logger;

    public override async Task Reply(ChatRequest request, IServerStreamWriter<ChatToken> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(request.TenantId, out var tenantId) || tenantId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        }
        Guid? convId = Guid.TryParse(request.ConversationId, out var cid) ? cid : null;

        // Tạo session sớm: mọi nhánh fail/timeout sau đây đều có chỗ Fail — tránh orphan "running".
        var session = AgentSession.Start(tenantId, agentId: null, conversationId: convId,
            goal: "chat-reply", startedAt: _clock.UtcNow);
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        using var replyCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        replyCts.CancelAfter(ChatReplyTimeout);
        var ct = replyCts.Token;

        Conversation? conversation = null;
        CoreChat.ChatAgentReply? reply = null;
        try
        {
            if (convId.HasValue)
            {
                conversation = await _db.Conversations
                    .IgnoreQueryFilters()
                    .Where(c => c.Id == convId.Value && c.TenantId == tenantId)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            }

            var sourcePlatform = conversation?.Platform;
            var matchedScenarioTemplate = await MatchScenarioTemplateAsync(
                tenantId,
                request.UserText,
                sourcePlatform,
                ct).ConfigureAwait(false);

            var history = request.History
                .Select((text, idx) => new CoreChat.ChatTurn(idx % 2 == 0 ? "user" : "assistant", text))
                .ToList();

            // Prompt custom + danh sach module KB lien ket tu cau hinh agent "chat-agent".
            // Prompt rong -> ChatAgent dung DefaultSystemPrompt; luon boc guardrail o ChatAgent.
            var (customPrompt, kbModuleCodes) = await LoadChatAgentSettingsAsync(tenantId, ct).ConfigureAwait(false);

            var contactFacts = await LoadContactFactsAsync(tenantId, conversation?.ContactId, ct).ConfigureAwait(false);

            await foreach (var chunk in _agent.StreamReplyAsync(
                               new CoreChat.ChatAgentRequest(
                                   tenantId,
                                   convId,
                                   KbModuleCode: null,
                                   request.UserText,
                                   history,
                                   SourcePlatform: sourcePlatform,
                                   MatchedScenarioTemplate: matchedScenarioTemplate,
                                   CustomSystemPrompt: customPrompt,
                                   ContactFacts: contactFacts,
                                   KbModuleCodes: kbModuleCodes),
                               ct).ConfigureAwait(false))
            {
                if (chunk.Final)
                {
                    reply = chunk.Reply;
                    await responseStream.WriteAsync(new ChatToken { Text = chunk.Text, Final = true }).ConfigureAwait(false);
                    continue;
                }

                await responseStream.WriteAsync(new ChatToken { Text = chunk.Text, Final = false }).ConfigureAwait(false);
            }

            if (reply is null)
                throw new InvalidOperationException("chat-agent stream completed without final reply");
        }
        catch (Exception ex)
        {
            await FailSessionAsync(session, ex, tenantId, convId).ConfigureAwait(false);
            if (ex is CoreChat.LlmConfigNotConfiguredException) throw;
            if (ex is RpcException) throw;
            if (ex is OperationCanceledException)
                throw new RpcException(new Status(StatusCode.DeadlineExceeded, "chat-agent timeout"));
            throw new RpcException(new Status(StatusCode.Internal, "chat-agent failure"));
        }

        try
        {
            // Echo guard: gateway/model lỗi đôi khi trả lại NGUYÊN tin khách làm reply (quan sát gpt-5.5
            // qua gateway 2026-07) — reply trùng tin nhập vào thì chặn hẳn, không cho vào hàng duyệt.
            if (!reply.Blocked && !string.IsNullOrWhiteSpace(reply.Text)
                && string.Equals(NormalizeForEchoCheck(reply.Text), NormalizeForEchoCheck(request.UserText), StringComparison.OrdinalIgnoreCase))
            {
                reply = reply with { Blocked = true, BlockReason = "echo_reply" };
            }

            // Review-gate P3 manual-mode: tenant bật RequireChatReplyApproval → hold MỌI reply chờ người duyệt
            // (khỏi tốn LLM critic — người là reviewer cuối). Tin sale gõ tay không đi qua đường này (QĐ5).
            var requireApproval = !reply.Blocked
                && _approvalPolicy is not null
                && await _approvalPolicy.IsRequiredAsync(tenantId, ct).ConfigureAwait(false);

            // Bypass gate P2 theo tenant (SkipChatReplyReview, QĐ user 2026-07-16): bật = gửi thẳng,
            // không critic. Safety cứng phía trên (echo/toxicity/injection) vẫn giữ nguyên.
            var skipReviewGate = !reply.Blocked
                && !requireApproval
                && _approvalPolicy is not null
                && await _approvalPolicy.IsReviewGateBypassedAsync(tenantId, ct).ConfigureAwait(false);

            // Review-gate P2 (QĐ2 tiered): tầng 1 deterministic (ChatReplyReviewTrigger — Escalate + nội dung
            // giá/cam kết) chạy 100%; tầng 2 LLM critic chỉ cho tin nghi ngờ. ContentReviewer fail-closed:
            // LLM down/timeout/JSON hỏng => needs_human — không bao giờ fail-open thành gửi (QĐ3).
            string? reviewVerdict = null;
            string? reviewReason = null;
            if (!reply.Blocked && !requireApproval && !skipReviewGate && !string.IsNullOrWhiteSpace(reply.Text)
                && CoreChat.ChatReplyReviewTrigger.NeedsLlmReview(reply))
            {
                using var reviewCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                reviewCts.CancelAfter(ChatReviewTimeout);
                try
                {
                    var review = await _reviewer.ReviewAsync(
                        tenantId, conversation?.Platform ?? "chat", reply.Text, reviewCts.Token).ConfigureAwait(false);
                    reviewVerdict = review.Verdict;
                    reviewReason = review.Reason;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Reviewer timeout/provider-side cancellation phải fail-closed thành draft chờ duyệt,
                    // không được làm hỏng toàn bộ auto-reply session hoặc để session running.
                    reviewVerdict = Clawbot.Agents.Core.Content.ContentReviewResult.NeedsHuman;
                    reviewReason = "review_timeout";
                }
            }

            // Trạng thái row tin nhắn: sent | pending_send | send_failed | pending_approval | blocked.
            var messageStatus = reply.Blocked
                ? "blocked"
                : requireApproval
                    ? "pending_approval"
                    : reviewVerdict == Clawbot.Agents.Core.Content.ContentReviewResult.RejectVerdict
                        ? "blocked"
                        : reviewVerdict == Clawbot.Agents.Core.Content.ContentReviewResult.NeedsHuman
                            ? "pending_approval"
                            : "sent";

            var phase = reply.Blocked
                ? "blocked"
                : requireApproval
                    ? "held_for_approval"
                    : messageStatus == "blocked"
                        ? "review_rejected"
                        : messageStatus == "pending_approval" ? "held_for_review" : "completed";
            session.AppendTrace("chat", "chat-agent", phase,
                $"intent={reply.Intent} blocked={reply.Blocked} latency={reply.LatencyMs}ms tokens={reply.InputTokens}/{reply.OutputTokens} usd={reply.UsdCost:0.0000} citations={reply.Citations.Count} lang={reply.Language} toxic_blocked={reply.ToxicityBlocked} spam={reply.SpamFlagged}{(reply.BlockReason is null ? "" : " block=" + reply.BlockReason)}{(skipReviewGate ? " review=bypassed" : reviewVerdict is null ? "" : $" review={reviewVerdict} reason={reviewReason}")}",
                _clock.UtcNow);

            var shouldSendToChannel = messageStatus == "sent"
                && conversation is { ExternalThreadId.Length: > 0 }
                && _channelAdapter is not null;

            Clawbot.Domain.Conversations.Message? persistedReply = null;
            if (convId.HasValue)
            {
                persistedReply = conversation?.AppendMessage("out", "agent", reply.Text, "text", _clock.UtcNow, status: messageStatus);
                if (shouldSendToChannel)
                    persistedReply?.MarkPendingSend();
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            // SPEC-16 P2-10: persist pending_send before calling the channel. Only a confirmed adapter completion may
            // transition the row to sent; every non-cancellation failure becomes send_failed for retry/audit.
            if (shouldSendToChannel
                && conversation is not null
                && persistedReply is not null
                && _channelAdapter is not null)
            {
                string? channelMessageId = null;
                var isChannelSendConfirmed = false;
                try
                {
                    channelMessageId = await _channelAdapter.SendAsync(
                        tenantId,
                        conversation.Platform,
                        conversation.ExternalThreadId,
                        reply.Text,
                        ct).ConfigureAwait(false);
                    isChannelSendConfirmed = true;
                }
                catch (OperationCanceledException ex)
                {
                    LogChannelSendFailure(_logger, ex, tenantId, convId, conversation.Platform);
                    persistedReply.MarkSendFailed();
                    session.AppendTrace("chat", "chat-agent", "send_failed",
                        $"channel send interrupted ({conversation.Platform})", _clock.UtcNow);
                    await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    LogChannelSendFailure(_logger, ex, tenantId, convId, conversation.Platform);
                    persistedReply.MarkSendFailed();
                    session.AppendTrace("chat", "chat-agent", "send_failed",
                        $"channel send failed ({conversation.Platform}): {ex.GetType().Name}", _clock.UtcNow);
                    await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }

                if (isChannelSendConfirmed)
                {
                    // Gan id phia kenh vao row da persist: poller dedup strict tin echo theo external_message_id
                    if (channelMessageId is not null)
                        persistedReply.SetExternalMessageId(channelMessageId);
                    persistedReply.MarkSent();
                    session.AppendTrace(
                        "chat",
                        "chat-agent",
                        "sent",
                        $"reply sent to channel {conversation.Platform}",
                        _clock.UtcNow);
                    await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            // Chỉ Finish SAU KHI message + trạng thái delivery đã persist. Nếu save/send bị cancel trước đó,
            // catch bên dưới vẫn thấy session Running và chốt Failed thay vì để DB kẹt running.
            session.Finish(_clock.UtcNow);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            // Part C.2: auto-score the lead from this customer message. Best-effort — never break the reply.
            await TryAutoScoreLeadAsync(tenantId, conversation, request.UserText, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (session.Status == AgentSessionStatuses.Running)
        {
            // Review/persist/send timeout hoặc lỗi sau stream: vẫn Fail session (đã Finish thì bỏ qua).
            await FailSessionAsync(session, ex, tenantId, convId).ConfigureAwait(false);
            if (ex is OperationCanceledException)
                throw new RpcException(new Status(StatusCode.DeadlineExceeded, "chat-agent timeout"));
            if (ex is RpcException) throw;
            throw new RpcException(new Status(StatusCode.Internal, "chat-agent failure"));
        }
    }

    private async Task FailSessionAsync(AgentSession session, Exception ex, Guid tenantId, Guid? convId)
    {
        LogChatFailure(_logger, ex, tenantId, convId);
        // Chỉ Fail khi còn running — tránh ghi đè completed/failed nếu race.
        if (session.Status == AgentSessionStatuses.Running)
        {
            var phase = ex is OperationCanceledException ? "timeout" : "error";
            session.AppendTrace("chat", "chat-agent", phase, ex.Message, _clock.UtcNow);
            session.Fail(_clock.UtcNow);
        }
        // CancellationToken.None: request bị hủy (client timeout/LLM chậm) vẫn phải chốt session —
        // save bằng token đã cancel sẽ throw ngay và để session kẹt "running" vĩnh viễn.
        await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task TryAutoScoreLeadAsync(Guid tenantId, Conversation? conversation, string userText, CancellationToken ct)
    {
        if (conversation?.ContactId is not { } contactId || string.IsNullOrWhiteSpace(userText))
            return;

        try
        {
            // fast_reply = customer answered quickly after our last outbound. Compare the latest
            // inbound (this message) against the most recent agent message that preceded it.
            var messageAt = await _db.Messages
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.ConversationId == conversation.Id && m.Direction == "in")
                .OrderByDescending(m => m.SentAt)
                .Select(m => (DateTimeOffset?)m.SentAt)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? _clock.UtcNow;

            var lastOutboundAt = await _db.Messages
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.ConversationId == conversation.Id
                    && m.Direction == "out" && m.SentAt <= messageAt)
                .OrderByDescending(m => m.SentAt)
                .Select(m => (DateTimeOffset?)m.SentAt)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            await _leadScorer.ScoreFromMessageAsync(
                new LeadAutoScoreInput(tenantId, contactId, conversation.Platform, userText, messageAt, lastOutboundAt),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLeadScoreFailure(_logger, ex, tenantId, conversation.Id);
        }
    }

    private const int MaxSkillPromptChars = 8000;

    // Prompt custom = config.SystemPrompt (ConfigJson.systemPrompt) + noi dung cac Tep ky nang da gan.
    // KbModuleCodes = KbModulesJson (checkbox "Kho tri thuc lien ket") — gioi han pham vi RAG retrieval.
    private async Task<(string? CustomPrompt, IReadOnlyList<string>? KbModuleCodes)> LoadChatAgentSettingsAsync(Guid tenantId, CancellationToken ct)
    {
        var agent = await _db.AgentConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Code == "chat-agent" && a.DeletedAt == null)
            .Select(a => new { a.ConfigJson, a.SkillFilesJson, a.KbModulesJson })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (agent is null) return (null, null);

        var custom = ExtractSystemPrompt(agent.ConfigJson);
        var skills = await LoadSkillContentAsync(tenantId, agent.SkillFilesJson, ct).ConfigureAwait(false);
        var kbModuleCodes = DeserializeKbModuleCodes(agent.KbModulesJson);

        if (string.IsNullOrWhiteSpace(skills)) return (custom, kbModuleCodes);
        return (string.IsNullOrWhiteSpace(custom) ? skills : $"{custom}\n\n{skills}", kbModuleCodes);
    }

    // JSON hong/rong -> null (= khong gioi han module, giu hanh vi cu thay vi chan het retrieval).
    private static string[]? DeserializeKbModuleCodes(string? kbModulesJson)
    {
        if (string.IsNullOrWhiteSpace(kbModulesJson)) return null;
        try
        {
            var codes = System.Text.Json.JsonSerializer.Deserialize<string[]>(kbModulesJson);
            if (codes is null) return null;
            var cleaned = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToArray();
            return cleaned.Length > 0 ? cleaned : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // ai-self-learning-memory Lop 2: top-10 facts active ve khach (moi nhat truoc, confidence >= 0.6).
    // Loi query -> bo qua, KHONG fail reply — memory la gia vi, khong phai xuong song.
    private async Task<IReadOnlyList<string>?> LoadContactFactsAsync(Guid tenantId, Guid? contactId, CancellationToken ct)
    {
        if (contactId is null) return null;
        try
        {
            var facts = await _db.ContactMemories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.ContactId == contactId && m.IsActive && m.Confidence >= 0.6m)
                .OrderByDescending(m => m.UpdatedAt)
                .Take(10)
                .Select(m => m.Fact)
                .ToListAsync(ct).ConfigureAwait(false);
            return facts.Count > 0 ? facts : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogContactFactsFailed(_logger, ex, tenantId, contactId.Value);
            return null;
        }
    }

    // So sánh echo: bỏ thẻ HTML + gom khoảng trắng — tin Pancake bọc <div> còn reply model là text trần.
    private static string NormalizeForEchoCheck(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " "),
            @"\s+", " ").Trim();

    private static string? ExtractSystemPrompt(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("systemPrompt", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
                ? p.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // Ghep noi dung cac skill file (theo Name luu trong SkillFilesJson) vao prompt, cap tong do dai.
    private async Task<string?> LoadSkillContentAsync(Guid tenantId, string? skillFilesJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(skillFilesJson)) return null;
        string[]? names;
        try
        {
            names = System.Text.Json.JsonSerializer.Deserialize<string[]>(skillFilesJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        if (names is null || names.Length == 0) return null;

        var files = await _db.SkillFiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && names.Contains(s.Name) && s.DeletedAt == null)
            .Select(s => new { s.Name, s.ContentMd })
            .ToListAsync(ct).ConfigureAwait(false);
        if (files.Count == 0) return null;

        var sb = new System.Text.StringBuilder("## Kỹ năng áp dụng\n");
        foreach (var file in files)
        {
            if (sb.Length + file.ContentMd.Length > MaxSkillPromptChars) break;
            sb.Append("### ").Append(file.Name).Append('\n').Append(file.ContentMd.Trim()).Append("\n\n");
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<string?> MatchScenarioTemplateAsync(
        Guid tenantId,
        string text,
        string? platform,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var candidates = await _db.ChatScenarios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct).ConfigureAwait(false);

        return ChatScenarioMatcher.Match(text, platform, candidates)?.ResponseTemplate;
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "Chat reply failed tenant={TenantId} conv={ConversationId}")]
    private static partial void LogChatFailure(ILogger logger, Exception ex, Guid tenantId, Guid? conversationId);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "Lead auto-score failed tenant={TenantId} conv={ConversationId}")]
    private static partial void LogLeadScoreFailure(ILogger logger, Exception ex, Guid tenantId, Guid conversationId);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Chat reply channel send failed tenant={TenantId} conv={ConversationId} platform={Platform}")]
    private static partial void LogChannelSendFailure(ILogger logger, Exception ex, Guid tenantId, Guid? conversationId, string platform);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning, Message = "Contact facts load failed tenant={TenantId} contact={ContactId} — reply continues without memory")]
    private static partial void LogContactFactsFailed(ILogger logger, Exception ex, Guid tenantId, Guid contactId);
}
