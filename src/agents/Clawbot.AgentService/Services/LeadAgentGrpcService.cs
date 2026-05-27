using Clawbot.Agents.Contracts.Lead;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class LeadAgentGrpcService : LeadAgent.LeadAgentBase
{
    public override Task<LeadScoreResponse> Score(LeadScoreRequest request, ServerCallContext context)
    {
        // TODO(student): real scoring via Lead agent + LLM.
        _ = request;
        return Task.FromResult(new LeadScoreResponse { Score = 0, Reason = "stub" });
    }
}
