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
    private static readonly TimeSpan ChatReviewTimeout = TimeSpan.FromSeconds(8);

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

        Conversation? conversation = null;
        if (convId.HasValue)
        {
            conversation = await _db.Conversations
                .IgnoreQueryFilters()
                .Where(c => c.Id == convId.Value && c.TenantId == tenantId)
                .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false);
        }

        var sourcePlatform = conversation?.Platform;
        var matchedScenarioTemplate = await MatchScenarioTemplateAsync(
            tenantId,
            request.UserText,
            sourcePlatform,
            context.CancellationToken).ConfigureAwait(false);

        var history = request.History
            .Select((text, idx) => new CoreChat.ChatTurn(idx % 2 == 0 ? "user" : "assistant", text))
            .ToList();

        // Prompt custom cua tenant tu cau hinh agent "chat-agent" (o "Huong dan tra loi").
        // Rong -> ChatAgent dung DefaultSystemPrompt; luon boc guardrail o ChatAgent.
        var customPrompt = await LoadChatSystemPromptAsync(tenantId, context.CancellationToken).ConfigureAwait(false);

        var session = AgentSession.Start(tenantId, agentId: null, conversationId: convId,
            goal: "chat-reply", startedAt: _clock.UtcNow);
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        CoreChat.ChatAgentReply? reply = null;
        try
        {
            await foreach (var chunk in _agent.StreamReplyAsync(
                               new CoreChat.ChatAgentRequest(
                                   tenantId,
                                   convId,
                                   KbModuleCode: null,
                                   request.UserText,
                                   history,
                                   SourcePlatform: sourcePlatform,
                                   MatchedScenarioTemplate: matchedScenarioTemplate,
                                   CustomSystemPrompt: customPrompt),
                               context.CancellationToken).ConfigureAwait(false))
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
            LogChatFailure(_logger, ex, tenantId, convId);
            session.AppendTrace("chat", "chat-agent", "error", ex.Message, _clock.UtcNow);
            session.Finish(_clock.UtcNow);
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
            // Let the typed "no provider config bound" error escape so the interceptor maps it to
            // FailedPrecondition/llm_config_not_configured instead of a generic Internal failure.
            if (ex is CoreChat.LlmConfigNotConfiguredException) throw;
            throw new RpcException(new Status(StatusCode.Internal, "chat-agent failure"));
        }

        // Review-gate P3 manual-mode: tenant bật RequireChatReplyApproval → hold MỌI reply chờ người duyệt
        // (khỏi tốn LLM critic — người là reviewer cuối). Tin sale gõ tay không đi qua đường này (QĐ5).
        var requireApproval = !reply.Blocked
            && _approvalPolicy is not null
            && await _approvalPolicy.IsRequiredAsync(tenantId, context.CancellationToken).ConfigureAwait(false);

        // Review-gate P2 (QĐ2 tiered): tầng 1 deterministic (ChatReplyReviewTrigger — Escalate + nội dung
        // giá/cam kết) chạy 100%; tầng 2 LLM critic chỉ cho tin nghi ngờ. ContentReviewer fail-closed:
        // LLM down/timeout/JSON hỏng => needs_human — không bao giờ fail-open thành gửi (QĐ3).
        string? reviewVerdict = null;
        string? reviewReason = null;
        if (!reply.Blocked && !requireApproval && !string.IsNullOrWhiteSpace(reply.Text)
            && CoreChat.ChatReplyReviewTrigger.NeedsLlmReview(reply))
        {
            using var reviewCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            reviewCts.CancelAfter(ChatReviewTimeout);
            var review = await _reviewer.ReviewAsync(
                tenantId, conversation?.Platform ?? "chat", reply.Text, reviewCts.Token).ConfigureAwait(false);
            reviewVerdict = review.Verdict;
            reviewReason = review.Reason;
        }

        // Trạng thái row tin nhắn: sent (gửi được) | pending_approval (chờ người duyệt) | blocked (chặn hẳn).
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
            $"intent={reply.Intent} blocked={reply.Blocked} latency={reply.LatencyMs}ms tokens={reply.InputTokens}/{reply.OutputTokens} usd={reply.UsdCost:0.0000} citations={reply.Citations.Count} lang={reply.Language} toxic_blocked={reply.ToxicityBlocked} spam={reply.SpamFlagged}{(reply.BlockReason is null ? "" : " block=" + reply.BlockReason)}{(reviewVerdict is null ? "" : $" review={reviewVerdict} reason={reviewReason}")}",
            _clock.UtcNow);
        session.Finish(_clock.UtcNow);

        Clawbot.Domain.Conversations.Message? persistedReply = null;
        if (convId.HasValue)
        {
            persistedReply = conversation?.AppendMessage("out", "agent", reply.Text, "text", _clock.UtcNow, status: messageStatus);
        }

        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        // SPEC-16 P2-10: physically send the AI reply to the channel after persisting it. Best-effort — a channel
        // failure surfaces a trace but does not undo the persisted reply. Only send when not blocked (safety/toxicity
        // gates), the review gate let it through (status=sent), and the conversation has an external thread id.
        if (!reply.Blocked && messageStatus == "sent" && conversation is { ExternalThreadId.Length: > 0 } && _channelAdapter is not null)
        {
            try
            {
                var channelMessageId = await _channelAdapter.SendAsync(conversation.ExternalThreadId, reply.Text, context.CancellationToken).ConfigureAwait(false);
                // Gan id phia kenh vao row da persist: poller dedup strict tin echo theo external_message_id
                if (channelMessageId is not null)
                    persistedReply?.SetExternalMessageId(channelMessageId);
                session.AppendTrace("chat", "chat-agent", "sent",
                    $"reply sent to channel {conversation.Platform} thread={conversation.ExternalThreadId}", _clock.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogChannelSendFailure(_logger, ex, tenantId, convId, conversation.Platform);
                session.AppendTrace("chat", "chat-agent", "send_failed",
                    $"channel send failed ({conversation.Platform}): {ex.Message}", _clock.UtcNow);
            }
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        }

        // Part C.2: auto-score the lead from this customer message. Best-effort — never break the reply.
        await TryAutoScoreLeadAsync(tenantId, conversation, request.UserText, context.CancellationToken).ConfigureAwait(false);
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
    private async Task<string?> LoadChatSystemPromptAsync(Guid tenantId, CancellationToken ct)
    {
        var agent = await _db.AgentConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Code == "chat-agent" && a.DeletedAt == null)
            .Select(a => new { a.ConfigJson, a.SkillFilesJson })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (agent is null) return null;

        var custom = ExtractSystemPrompt(agent.ConfigJson);
        var skills = await LoadSkillContentAsync(tenantId, agent.SkillFilesJson, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(skills)) return custom;
        return string.IsNullOrWhiteSpace(custom) ? skills : $"{custom}\n\n{skills}";
    }

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
}
