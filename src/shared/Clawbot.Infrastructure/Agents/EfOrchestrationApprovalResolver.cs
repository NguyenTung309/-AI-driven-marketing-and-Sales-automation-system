using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

public sealed class EfOrchestrationApprovalResolver(AppDbContext db) : IOrchestrationApprovalResolver
{
    private readonly AppDbContext _db = db;

    public async Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return false;
        return await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (bool?)t.RequireOrchestrationApproval)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? false;
    }
}
