using Clawbot.Agents.Contracts.SaleAssist;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class SaleAssistAgentGrpcService : SaleAssistAgent.SaleAssistAgentBase
{
    public override Task<DraftResponse> Draft(DraftRequest request, ServerCallContext context)
    {
        _ = request;
        // TODO(student): assemble context (last N messages + lead score + scenario match) and call Semantic Kernel.
        return Task.FromResult(new DraftResponse { DraftText = "(stub) draft", SuggestedAction = "ask_goal", LeadScore = 0 });
    }

    public override Task<SummarizeResponse> Summarize(SummarizeRequest request, ServerCallContext context)
    {
        _ = request;
        // TODO(student): summarize conversation via LLM.
        return Task.FromResult(new SummarizeResponse { Summary = "(stub) summary" });
    }
}
