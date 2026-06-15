using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// SPEC-11 D1/D5 — idempotently provisions the 5 fixed-Id roles into BOTH the Identity
/// store (AspNetRoles) and the domain <c>roles</c> table (so role_permissions.role_id, which
/// FKs to roles(id), can carry the same fixed Id the JWT does), then seeds the permission
/// matrix into <c>permissions</c> + <c>role_permissions</c>.
/// </summary>
public static partial class RbacSeeder
{
    // SPEC-11 §6 — fixed role Ids (constants, never NewGuid).
    public const string Admin = "Admin";
    public const string Sale = "Sale";
    public const string Marketer = "Marketer";
    public const string QA = "QA";
    public const string Viewer = "Viewer";
    public const string SalesLead = "SalesLead";

    public static readonly IReadOnlyDictionary<string, Guid> RoleIds = new Dictionary<string, Guid>
    {
        [Admin] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        [SalesLead] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        [Sale] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        [Marketer] = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        [QA] = Guid.Parse("55555555-5555-5555-5555-555555555555"),
        [Viewer] = Guid.Parse("66666666-6666-6666-6666-666666666666"),
    };

    public static readonly IReadOnlyList<string> DefaultRoles = RoleIds.Keys.ToArray();

    private static readonly string[] All = [Admin, SalesLead, Sale, Marketer, QA, Viewer];

    // SPEC-11 §6 permission matrix.
    private static readonly (string Code, string[] Roles)[] Matrix =
    [
        ("conversations:read", All),
        ("conversations:write", [Admin, SalesLead, Sale]),
        ("leads:read", All),
        ("leads:write", [Admin, SalesLead, Sale]),
        ("content:read", All),
        ("content:write", [Admin, Marketer]),
        ("ads:read", [Admin, Marketer, QA, Viewer]),
        ("ads:write", [Admin, Marketer]),
        ("analytics:read", All),
        ("kb:read", All),
        ("kb:write", [Admin, SalesLead, Marketer, QA]),
        ("docs:read", All),
        ("docs:write", [Admin, SalesLead, Sale, Marketer]),
        ("sale-assist:use", [Admin, SalesLead, Sale]),
        ("chat-scenarios:read", All),
        ("chat-scenarios:write", [Admin, SalesLead, Marketer, QA]),
        ("channels:manage", [Admin]),
        ("api-keys:manage", [Admin]),
        ("rbac:manage", [Admin]),
        ("users:manage", [Admin]),
        ("system:config", [Admin]),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var roleManager = sp.GetRequiredService<RoleManager<AppRole>>();
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("RbacSeeder");
        var now = DateTimeOffset.UtcNow;

        await SeedIdentityRolesAsync(roleManager, logger);
        var tenantId = await EnsureDefaultTenantAsync(db, now, ct);
        await SeedDomainRolesAsync(db, tenantId, now, ct);
        await SeedPermissionsAsync(db, ct);
        await SeedRolePermissionsAsync(db, ct);

        var permissionCount = await db.Permissions.CountAsync(ct);
        LogPermissionCount(logger, permissionCount);
    }

    private static async Task SeedIdentityRolesAsync(RoleManager<AppRole> roleManager, ILogger logger)
    {
        foreach (var (name, id) in RoleIds)
        {
            if (await roleManager.FindByNameAsync(name) is not null) continue;
            var result = await roleManager.CreateAsync(new AppRole(name) { Id = id });
            if (!result.Succeeded)
                LogRoleSeedFailed(logger, name, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task<Guid> EnsureDefaultTenantAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == DevDataSeeder.TenantSlug, ct);
        if (tenant is not null) return tenant.Id;

        tenant = Tenant.Create(DevDataSeeder.TenantSlug, "Default Tenant", "free", now);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
        return tenant.Id;
    }

    private static async Task SeedDomainRolesAsync(AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        // IgnoreQueryFilters: seeding runs outside any HTTP/tenant scope, so the tenant
        // global filter would otherwise hide existing rows and break idempotency.
        var existing = await db.RbacRoles.IgnoreQueryFilters().Select(r => r.Id).ToListAsync(ct);
        var have = existing.ToHashSet();
        foreach (var (name, id) in RoleIds)
        {
            if (have.Contains(id)) continue;
            db.RbacRoles.Add(Role.Seed(id, tenantId, name, now));
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        var have = (await db.Permissions.Select(p => p.Code).ToListAsync(ct)).ToHashSet();
        foreach (var code in Matrix.Select(m => m.Code).Distinct())
        {
            if (have.Contains(code)) continue;
            db.Permissions.Add(Permission.Create(code));
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRolePermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        var permIdByCode = await db.Permissions.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var have = (await db.RolePermissions.Select(rp => new { rp.RoleId, rp.PermissionId }).ToListAsync(ct))
            .Select(x => (x.RoleId, x.PermissionId)).ToHashSet();

        foreach (var (code, roles) in Matrix)
        {
            if (!permIdByCode.TryGetValue(code, out var permId)) continue;
            foreach (var role in roles)
            {
                var roleId = RoleIds[role];
                if (have.Contains((roleId, permId))) continue;
                db.RolePermissions.Add(RolePermission.Create(roleId, permId));
            }
        }
        await db.SaveChangesAsync(ct);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Failed to seed role {RoleName}: {Errors}")]
    private static partial void LogRoleSeedFailed(ILogger logger, string roleName, string errors);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "RbacSeeder: {Count} permissions registered")]
    private static partial void LogPermissionCount(ILogger logger, int count);
}
