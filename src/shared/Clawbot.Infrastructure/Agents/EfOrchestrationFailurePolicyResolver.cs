using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

public sealed class EfOrchestrationFailurePolicyResolver(AppDbContext db) : IOrchestrationFailurePolicyResolver
{
    private readonly AppDbContext _db = db;

    public async Task<string> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return OrchestratorFailurePolicies.Pause;
        var raw = await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.OrchestratorFailurePolicy)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return OrchestratorFailurePolicies.Normalize(raw);
    }
}
