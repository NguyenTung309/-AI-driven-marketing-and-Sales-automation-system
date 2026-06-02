using Clawbot.Agents.Contracts.Chat;
using Clawbot.Domain.Agents;
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

        var history = request.History
            .Select((text, idx) => new CoreChat.ChatTurn(idx % 2 == 0 ? "user" : "assistant", text))
            .ToList();

        var session = AgentSession.Start(tenantId, agentId: null, conversationId: convId,
            goal: "chat-reply", startedAt: _clock.UtcNow);
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        CoreChat.ChatAgentReply reply;
        try
        {
            reply = await _agent.ReplyAsync(
                new CoreChat.ChatAgentRequest(tenantId, convId, KbModuleCode: null, request.UserText, history),
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogChatFailure(_logger, ex, tenantId, convId);
            session.AppendTrace("chat", "chat-agent", "error", ex.Message, _clock.UtcNow);
            session.Finish(_clock.UtcNow);
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
            throw new RpcException(new Status(StatusCode.Internal, "chat-agent failure"));
        }

        session.AppendTrace("chat", "chat-agent", "completed",
            $"latency={reply.LatencyMs}ms tokens={reply.InputTokens}/{reply.OutputTokens} usd={reply.UsdCost:0.0000} citations={reply.Citations.Count}",
            _clock.UtcNow);
        session.Finish(_clock.UtcNow);

        if (convId.HasValue)
        {
            var conv = await _db.Conversations
                .IgnoreQueryFilters()
                .Where(c => c.Id == convId.Value && c.TenantId == tenantId)
                .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false);
            conv?.AppendMessage("out", "agent", reply.Text, "text", _clock.UtcNow);
        }

        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        await responseStream.WriteAsync(new ChatToken { Text = reply.Text, Final = true }).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "Chat reply failed tenant={TenantId} conv={ConversationId}")]
    private static partial void LogChatFailure(ILogger logger, Exception ex, Guid tenantId, Guid? conversationId);
}
