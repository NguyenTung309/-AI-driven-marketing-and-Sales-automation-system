using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// Idempotently provisions Identity roles (Admin/Sale/Marketer/QA/Viewer) and
/// seeds the role→permission mapping so JWT tokens carry correct perm claims.
/// Custom tenant-scoped Role rows are created per tenant on demand by RolesEndpoints.
/// </summary>
public static partial class RbacSeeder
{
    public static readonly IReadOnlyList<string> DefaultRoles =
        new[] { "Admin", "Sale", "Marketer", "QA", "Viewer" };

    private static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        ["Admin"] =
        [
            "inbox.read", "inbox.assign",
            "kb.read", "kb.write", "kb.deploy",
            "agent.read", "agent.manage",
            "lead.read", "lead.write",
            "content.read", "content.write", "content.approve",
            "docs.generate",
            "ads.read", "ads.manage",
            "analytics.read",
            "admin.system", "admin.audit",
        ],
        ["Sale"] =
        [
            "inbox.read", "inbox.assign",
            "lead.read", "lead.write",
            "content.read",
            "docs.generate",
            "analytics.read",
        ],
        ["Marketer"] =
        [
            "content.read", "content.write", "content.approve",
            "ads.read", "ads.manage",
            "analytics.read",
        ],
        ["QA"] =
        [
            "kb.read", "kb.write",
            "content.read",
            "analytics.read",
        ],
        ["Viewer"] =
        [
            "inbox.read",
            "lead.read",
            "content.read",
            "analytics.read",
        ],
    };

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var roleManager = sp.GetRequiredService<RoleManager<AppRole>>();
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("RbacSeeder");

        foreach (var name in DefaultRoles)
        {
            if (await roleManager.RoleExistsAsync(name)) continue;
            var role = new AppRole(name) { Id = Guid.NewGuid() };
            var result = await roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                LogRoleSeedFailed(logger, name,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        var permCount = await db.Permissions.CountAsync(ct);
        LogPermissionCount(logger, permCount);

        var tenants = await db.Tenants.IgnoreQueryFilters().ToListAsync(ct);
        var perms = await db.Permissions.ToListAsync(ct);
        var permLookup = perms.ToDictionary(p => p.Code, p => p.Id);
        var now = DateTimeOffset.UtcNow;

        foreach (var tenant in tenants)
        {
            var domainRoles = await db.RbacRoles
                .IgnoreQueryFilters()
                .Where(r => r.TenantId == tenant.Id)
                .ToListAsync(ct);

            foreach (var roleName in DefaultRoles)
            {
                var domainRole = domainRoles.FirstOrDefault(r => r.Name == roleName);
                if (domainRole is null)
                {
                    domainRole = Role.Create(tenant.Id, roleName, $"Default {roleName} role", true, now);
                    db.RbacRoles.Add(domainRole);
                    domainRoles.Add(domainRole);
                }

                if (!RolePermissions.TryGetValue(roleName, out var permCodes)) continue;

                var existingLinks = await db.RolePermissions
                    .Where(rp => rp.RoleId == domainRole.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync(ct);

                foreach (var code in permCodes)
                {
                    if (!permLookup.TryGetValue(code, out var permId))
                    {
                        LogPermNotFound(logger, code, roleName);
                        continue;
                    }
                    if (existingLinks.Contains(permId)) continue;

                    db.RolePermissions.Add(RolePermission.Create(domainRole.Id, permId));
                }
            }
        }

        var saved = await db.SaveChangesAsync(ct);
        if (saved > 0) LogRolePermsSeeded(logger, saved);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Failed to seed role {RoleName}: {Errors}")]
    private static partial void LogRoleSeedFailed(ILogger logger, string roleName, string errors);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "RbacSeeder: {Count} permissions registered")]
    private static partial void LogPermissionCount(ILogger logger, int count);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Permission '{Code}' not found in DB for role '{RoleName}'")]
    private static partial void LogPermNotFound(ILogger logger, string code, string roleName);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information,
        Message = "RbacSeeder: {Count} role→permission links seeded")]
    private static partial void LogRolePermsSeeded(ILogger logger, int count);
}
