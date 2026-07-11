using Clawbot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Jobs;

// Review-gate P4: ai nhận escalation khi bài chờ review sát/quá giờ đăng.
public interface IContentReviewEscalationRecipientResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}

// Marketer giữ content.approve (RbacSeeder) — họ + Admin là người gỡ bài kẹt review.
// Rỗng cả hai role -> caller fallback tenant-broadcast (mirror SalesLeadIdleEscalationRecipientResolver).
public sealed class ContentReviewEscalationRecipientResolver(UserManager<AppUser> users)
    : IContentReviewEscalationRecipientResolver
{
    public const string MarketerRoleName = "Marketer";
    public const string AdminRoleName = "Admin";

    public async Task<IReadOnlyList<Guid>> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        _ = ct;
        var marketers = await users.GetUsersInRoleAsync(MarketerRoleName).ConfigureAwait(false);
        var admins = await users.GetUsersInRoleAsync(AdminRoleName).ConfigureAwait(false);
        return marketers.Concat(admins)
            .Where(user => user.TenantId == tenantId && user.IsActive)
            .Select(user => user.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }
}
