using Clawbot.Agents.Contracts.Research;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using CoreResearch = Clawbot.Agents.Core.Research;

namespace Clawbot.AgentService.Services;

public sealed class ResearchAgentGrpcService(
    ITenantTrendScanner scanner,
    IClock clock) : ResearchAgent.ResearchAgentBase
{
    private readonly ITenantTrendScanner _scanner = scanner;
    private readonly IClock _clock = clock;

    public override async Task<TrendResponse> WeeklyTrends(TrendRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var tenantId = ParseTenantId(request.TenantId);
        var weekOf = ParseWeekOf(request.WeekOf);
        var trends = await _scanner.ScanAndPersistAsync(tenantId, weekOf, context.CancellationToken).ConfigureAwait(false);

        var response = new TrendResponse();
        response.Trends.AddRange(trends.Select(ToTrendItem));
        return response;
    }

    private string ParseWeekOf(string weekOf)
    {
        try
        {
            return ContentTrendBriefFormatter.NormalizeWeekOfOrCurrent(weekOf, _clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    private static Guid ParseTenantId(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var parsed) || parsed == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));

        return parsed;
    }

    private static TrendItem ToTrendItem(CoreResearch.ScoredTrend trend)
    {
        var item = new TrendItem
        {
            Topic = trend.Topic,
            Source = trend.Source,
            Metric = trend.Metric,
            RelevanceScore = trend.RelevanceScore,
        };
        item.ContentIdeas.AddRange(trend.ContentIdeas);
        return item;
    }
}
