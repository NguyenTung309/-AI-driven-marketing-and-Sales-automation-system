using Clawbot.Agents.Contracts.Chat;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class ChatAgentGrpcService : ChatAgent.ChatAgentBase
{
    public override async Task Reply(ChatRequest request, IServerStreamWriter<ChatToken> responseStream, ServerCallContext context)
    {
        // TODO(student): integrate Semantic Kernel + RAG + IVectorStore.
        _ = request;
        await responseStream.WriteAsync(new ChatToken { Text = "(stub) hello", Final = true });
    }
}
