using Clawbot.Agents.Contracts.Content;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class ContentAgentGrpcService : ContentAgent.ContentAgentBase
{
    public override Task<ContentResponse> Generate(ContentRequest request, ServerCallContext context)
    {
        // TODO(student): call LLM via Semantic Kernel.
        _ = request;
        return Task.FromResult(new ContentResponse
        {
            ContentId = Guid.NewGuid().ToString(),
            Title = "TBD",
            Body = "TBD",
        });
    }
}
