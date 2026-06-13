using Clawbot.Agents.Core.Lead;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Leads;

// Loads eligible (active) Sale-role users plus their current open workload so the
// least-busy strategy can pick the freest sale. Uses IgnoreQueryFilters + explicit tenant
// filter because this also runs from a MassTransit consumer scope (no ambient HTTP tenant).
public sealed class EfAssignmentPoolSource(UserManager<AppUser> users, AppDbContext db) : IAssignmentPoolSource
{
    private const string SaleRoleName = "Sale";

    private readonly UserManager<AppUser> _users = users;
    private readonly AppDbContext _db = db;

    public async Task<AssignmentPool> LoadAsync(Guid tenantId, CancellationToken ct = default)
    {
        var sales = await _users.GetUsersInRoleAsync(SaleRoleName).ConfigureAwait(false);
        var activeSaleIds = sales.Where(u => u.IsActive).Select(u => u.Id).ToList();
        if (activeSaleIds.Count == 0) return new AssignmentPool([]);

        // Open conversations assigned (anything not resolved).
        var convLoad = await _db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.AssignedTo != null
                && c.Status != "resolved" && c.DeletedAt == null)
            .GroupBy(c => c.AssignedTo!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        // Open leads owned in active stages (warm/hot).
        var leadLoad = await _db.Leads.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.OwnerUserId != null
                && (l.Stage == "warm" || l.Stage == "hot") && l.DeletedAt == null)
            .GroupBy(l => l.OwnerUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var convMap = convLoad.ToDictionary(x => x.UserId, x => x.Count);
        var leadMap = leadLoad.ToDictionary(x => x.UserId, x => x.Count);

        var candidates = activeSaleIds
            .Select(id => new AssignmentCandidate(
                id,
                (convMap.TryGetValue(id, out var c) ? c : 0) + (leadMap.TryGetValue(id, out var l) ? l : 0)))
            .ToList();

        return new AssignmentPool(candidates);
    }
}
