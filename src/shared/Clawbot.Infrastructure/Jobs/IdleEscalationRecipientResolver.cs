using Clawbot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Jobs;

public interface IIdleEscalationRecipientResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class SalesLeadIdleEscalationRecipientResolver(UserManager<AppUser> users)
    : IIdleEscalationRecipientResolver
{
    public const string SalesLeadRoleName = "SalesLead";

    public async Task<IReadOnlyList<Guid>> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        _ = ct;
        var salesLeads = await users.GetUsersInRoleAsync(SalesLeadRoleName).ConfigureAwait(false);
        return salesLeads
            .Where(user => user.TenantId == tenantId && user.IsActive)
            .Select(user => user.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }
}
