using Clawbot.Agents.Contracts.Chat;
using Grpc.Core;

namespace Clawbot.Infrastructure.Messaging;

// Thin seam over the ChatAgent gRPC stream so the consumer stays testable.
public interface IChatAutoReplyGateway
{
    Task ReplyAsync(Guid tenantId, Guid conversationId, string userText, IReadOnlyList<string> history, CancellationToken ct);
}

public sealed class GrpcChatAutoReplyGateway(ChatAgent.ChatAgentClient client) : IChatAutoReplyGateway
{
    // Client-side hard stop for auto-reply. Slightly above ChatAgentGrpcService.ChatReplyTimeout (90s)
    // so the server can Fail the session before the client abandons the stream.
    private static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(100);

    private readonly ChatAgent.ChatAgentClient _client = client;

    // ChatAgent.Reply persists the out-message and sends it to the channel (SPEC-16 P2-10);
    // the caller only needs the stream drained to completion.
    public async Task ReplyAsync(Guid tenantId, Guid conversationId, string userText, IReadOnlyList<string> history, CancellationToken ct)
    {
        var request = new ChatRequest
        {
            TenantId = tenantId.ToString(),
            ConversationId = conversationId.ToString(),
            UserText = userText,
        };
        request.History.AddRange(history);

        // Deadline + linked CTS: không để ChannelInboundMessageConsumer treo vô hạn khi Agent/LLM hang.
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(ReplyDeadline);
        var callOptions = new CallOptions(deadline: DateTime.UtcNow.Add(ReplyDeadline), cancellationToken: deadlineCts.Token);

        using var call = _client.Reply(request, callOptions);
        await foreach (var _ in call.ResponseStream.ReadAllAsync(deadlineCts.Token).ConfigureAwait(false))
        {
        }
    }
}
