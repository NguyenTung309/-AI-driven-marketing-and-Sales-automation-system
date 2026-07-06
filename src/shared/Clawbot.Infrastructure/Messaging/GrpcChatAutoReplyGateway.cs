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

        using var call = _client.Reply(request, cancellationToken: ct);
        await foreach (var _ in call.ResponseStream.ReadAllAsync(ct).ConfigureAwait(false))
        {
        }
    }
}
