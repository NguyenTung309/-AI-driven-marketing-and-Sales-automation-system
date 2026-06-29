using Clawbot.Agents.Core.Rag;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

public sealed class ActiveKbVersionResolver(AppDbContext db) : IActiveKbVersionResolver
{
    public async Task<IReadOnlySet<string>> ResolveActiveVersionIdsAsync(Guid tenantId, string? moduleCode, CancellationToken ct = default)
    {
        var query =
            from version in db.KbVersions.IgnoreQueryFilters().AsNoTracking()
            join module in db.KbModules.IgnoreQueryFilters().AsNoTracking() on version.KbModuleId equals module.Id
            where module.TenantId == tenantId
                && module.DeletedAt == null
                && version.Status == "deployed"
            select new { version.Id, module.Code };

        if (!string.IsNullOrWhiteSpace(moduleCode))
            query = query.Where(row => row.Code == moduleCode);

        var ids = await query.Select(row => row.Id.ToString()).ToListAsync(ct).ConfigureAwait(false);
        return ids.ToHashSet(StringComparer.Ordinal);
    }
}
