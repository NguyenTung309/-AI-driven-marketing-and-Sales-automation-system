using Clawbot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Leads;

/// <summary>
/// Resolve người nhận notify cho lead: ưu tiên owner (cùng tenant + active);
/// null-owner / owner invalid thì Admin rồi SalesLead của tenant.
/// Không bao giờ trả null để caller broadcast tenant-wide — null nghĩa là bỏ notify.
/// </summary>
public interface ILeadNotificationRecipientResolver
{
    Task<Guid?> ResolveAsync(Guid tenantId, Guid? ownerUserId, CancellationToken ct = default);
}

public sealed class LeadNotificationRecipientResolver(UserManager<AppUser> users) : ILeadNotificationRecipientResolver
{
    public const string AdminRoleName = "Admin";
    public const string SalesLeadRoleName = "SalesLead";

    public async Task<Guid?> ResolveAsync(Guid tenantId, Guid? ownerUserId, CancellationToken ct = default)
    {
        if (ownerUserId is { } owner && owner != Guid.Empty)
        {
            // Không tin raw owner GUID — phải thuộc tenant và còn active (chống cross-tenant assign).
            var ownerUser = await users.Users
                .FirstOrDefaultAsync(u => u.Id == owner, ct)
                .ConfigureAwait(false);
            if (ownerUser is not null && ownerUser.TenantId == tenantId && ownerUser.IsActive)
                return owner;
        }

        var admins = await users.GetUsersInRoleAsync(AdminRoleName).ConfigureAwait(false);
        var adminId = admins
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Select(u => u.Id)
            .OrderBy(id => id)
            .FirstOrDefault();
        if (adminId != Guid.Empty)
            return adminId;

        var salesLeads = await users.GetUsersInRoleAsync(SalesLeadRoleName).ConfigureAwait(false);
        var salesLeadId = salesLeads
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Select(u => u.Id)
            .OrderBy(id => id)
            .FirstOrDefault();
        return salesLeadId == Guid.Empty ? null : salesLeadId;
    }
}
