using Clawbot.Agents.Contracts.Research;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class ResearchAgentGrpcService : ResearchAgent.ResearchAgentBase
{
    public override Task<TrendResponse> WeeklyTrends(TrendRequest request, ServerCallContext context)
    {
        _ = request;
        // TODO(student): scrape TikTok / YouTube / Baidu / Google Trends, score relevance, persist content_briefs.
        return Task.FromResult(new TrendResponse());
    }
}
