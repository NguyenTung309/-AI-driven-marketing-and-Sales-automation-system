using Clawbot.Agents.Core.Rag;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

public sealed class ActiveKbVersionResolver(AppDbContext db) : IActiveKbVersionResolver
{
    public async Task<IReadOnlySet<string>> ResolveActiveVersionIdsAsync(Guid tenantId, IReadOnlyCollection<string>? moduleCodes, CancellationToken ct = default)
    {
        var query =
            from version in db.KbVersions.IgnoreQueryFilters().AsNoTracking()
            join module in db.KbModules.IgnoreQueryFilters().AsNoTracking() on version.KbModuleId equals module.Id
            where module.TenantId == tenantId
                && module.DeletedAt == null
                && version.Status == "deployed"
            select new { version.Id, module.Code };

        if (moduleCodes is { Count: > 0 })
            query = query.Where(row => moduleCodes.Contains(row.Code));

        // Materialize the Guids, then ToString() in C# (lowercase). Doing .Id.ToString() inside the
        // query makes EF emit SQL Server CONVERT, which returns UPPERCASE GUIDs — those then fail the
        // case-sensitive Qdrant keyword match against the lowercase kb_version_id stored at deploy time.
        var ids = await query.Select(row => row.Id).ToListAsync(ct).ConfigureAwait(false);
        return ids.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
    }
}
