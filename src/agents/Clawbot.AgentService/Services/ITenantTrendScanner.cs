using CoreResearch = Clawbot.Agents.Core.Research;

namespace Clawbot.AgentService.Services;

public interface ITenantTrendScanner
{
    Task<IReadOnlyList<CoreResearch.ScoredTrend>> ScanAndPersistAsync(
        Guid tenantId,
        string weekOf,
        CancellationToken ct = default);
}
