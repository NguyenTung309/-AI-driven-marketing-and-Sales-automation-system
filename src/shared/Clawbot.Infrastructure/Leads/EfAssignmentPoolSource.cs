using Clawbot.Agents.Core.Lead;
using Clawbot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Leads;

public sealed class EfAssignmentPoolSource(UserManager<AppUser> users) : IAssignmentPoolSource
{
    private const string SaleRoleName = "Sale";

    private readonly UserManager<AppUser> _users = users;

    public async Task<AssignmentPool> LoadAsync(Guid tenantId, CancellationToken ct = default)
    {
        _ = tenantId; // AppUser is not tenant-scoped in current schema; future: filter by tenant_id claim/join.
        _ = ct;
        var sales = await _users.GetUsersInRoleAsync(SaleRoleName).ConfigureAwait(false);
        return new AssignmentPool(sales.Select(u => u.Id).ToList());
    }
}
