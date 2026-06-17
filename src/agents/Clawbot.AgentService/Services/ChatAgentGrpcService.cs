using Clawbot.Agents.Contracts.Chat;
using Clawbot.Domain.Agents;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
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
    ILogger<ChatAgentGrpcService> logger) : ChatAgent.ChatAgentBase
{
    private readonly CoreChat.ChatAgent _agent = agent;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
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
}
