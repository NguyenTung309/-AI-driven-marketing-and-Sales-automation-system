using Clawbot.Agents.Contracts.Ads;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class AdsAgentGrpcService : AdsAgent.AdsAgentBase
{
    public override Task<AdsEvaluateResponse> Evaluate(AdsEvaluateRequest request, ServerCallContext context)
    {
        _ = request;
        // TODO(student): pull ads_rules for tenant + platform, evaluate against current campaign metrics, execute action via Meta/TikTok API,
        // append ads_actions row, return executed list.
        return Task.FromResult(new AdsEvaluateResponse());
    }
}
