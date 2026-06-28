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
    AppDbContext db,
    IClock clock,
    LeadAutoScorer leadScorer,
    ILogger<ChatAgentGrpcService> logger,
    IChannelAdapter? channelAdapter = null) : ChatAgent.ChatAgentBase
{
    private readonly CoreChat.ChatAgent _agent = agent;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly LeadAutoScorer _leadScorer = leadScorer;
    private readonly IChannelAdapter? _channelAdapter = channelAdapter;
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
                                   MatchedScenarioTemplate: matchedScenarioTemplate),
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

        var phase = reply.Blocked ? "blocked" : "completed";
        session.AppendTrace("chat", "chat-agent", phase,
            $"intent={reply.Intent} blocked={reply.Blocked} latency={reply.LatencyMs}ms tokens={reply.InputTokens}/{reply.OutputTokens} usd={reply.UsdCost:0.0000} citations={reply.Citations.Count} lang={reply.Language} toxic_blocked={reply.ToxicityBlocked} spam={reply.SpamFlagged}{(reply.BlockReason is null ? "" : " block=" + reply.BlockReason)}",
            _clock.UtcNow);
        session.Finish(_clock.UtcNow);

        if (convId.HasValue)
        {
            conversation?.AppendMessage("out", "agent", reply.Text, "text", _clock.UtcNow);
        }

        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        // SPEC-16 P2-10: physically send the AI reply to the channel after persisting it. Best-effort — a channel
        // failure surfaces a trace but does not undo the persisted reply. Only send when not blocked (safety/toxicity
        // gates) and the conversation has an external thread id to address.
        if (!reply.Blocked && conversation is { ExternalThreadId.Length: > 0 } && _channelAdapter is not null)
        {
            try
            {
                await _channelAdapter.SendAsync(conversation.ExternalThreadId, reply.Text, context.CancellationToken).ConfigureAwait(false);
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
