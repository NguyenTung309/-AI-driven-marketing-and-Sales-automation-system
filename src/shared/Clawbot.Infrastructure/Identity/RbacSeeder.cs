using Clawbot.Domain.Agents;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// Idempotently provisions fixed Identity/domain roles and seeds the runtime permission matrix.
/// </summary>
public static partial class RbacSeeder
{
    public const string Admin = "Admin";
    public const string Sale = "Sale";
    public const string Marketer = "Marketer";
    public const string QA = "QA";
    public const string Viewer = "Viewer";
    public const string SalesLead = "SalesLead";

    public static readonly IReadOnlyDictionary<string, Guid> RoleIds = new Dictionary<string, Guid>
    {
        [Admin] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        [Sale] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        [Marketer] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        [QA] = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        [Viewer] = Guid.Parse("55555555-5555-5555-5555-555555555555"),
        [SalesLead] = Guid.Parse("77777777-7777-7777-7777-777777777777"),
    };

    public static readonly IReadOnlyList<string> DefaultRoles = RoleIds.Keys.ToArray();

    private static readonly string[] All = [Admin, SalesLead, Sale, Marketer, QA, Viewer];

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
        ("admin:inboxes", [Admin]),
    ];

    private static readonly (string Code, string DisplayName, string AgentType)[] DefaultAgents =
    [
        ("chat-agent", "Agent-Chat", "chat"),
        ("sale-assist", "Agent-SaleAssist", "sale_assist"),
        ("lead-agent", "Agent-Lead", "lead"),
        ("content-agent", "Agent-Content", "content"),
        ("research-agent", "Agent-Research", "research"),
        ("docs-agent", "Agent-Docs", "docs"),
        ("report-agent", "Agent-Report", "report"),
        ("ads-agent", "Agent-Ads", "ads"),
    ];

    private static readonly Dictionary<string, string[]> LegacyRolePermissions = new()
    {
        [Admin] =
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
        [Sale] =
        [
            "inbox.read", "inbox.assign",
            "lead.read", "lead.write",
            "content.read",
            "docs.generate",
            "analytics.read",
        ],
        [SalesLead] =
        [
            "inbox.read", "inbox.assign",
            "lead.read", "lead.write",
            "content.read",
            "docs.generate",
            "analytics.read",
        ],
        [Marketer] =
        [
            "content.read", "content.write", "content.approve",
            "ads.read", "ads.manage",
            "analytics.read",
        ],
        [QA] =
        [
            "kb.read", "kb.write",
            "content.read",
            "analytics.read",
        ],
        [Viewer] =
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
        var now = DateTimeOffset.UtcNow;

        await SeedIdentityRolesAsync(roleManager, logger);
        var tenantId = await EnsureDefaultTenantAsync(db, now, ct);
        await SeedDomainRolesAsync(db, tenantId, now, ct);
        await SeedPermissionsAsync(db, ct);
        await SeedRolePermissionsAsync(db, ct);
        await SeedTenantResourcesAsync(db, now, logger, ct);

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
        var codes = Matrix.Select(m => m.Code)
            .Concat(LegacyRolePermissions.SelectMany(kv => kv.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var have = (await db.Permissions.Select(p => p.Code).ToListAsync(ct)).ToHashSet();
        foreach (var code in codes)
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

        foreach (var (role, codes) in LegacyRolePermissions)
        {
            var roleId = RoleIds[role];
            foreach (var code in codes)
            {
                if (!permIdByCode.TryGetValue(code, out var permId)) continue;
                if (have.Contains((roleId, permId))) continue;
                db.RolePermissions.Add(RolePermission.Create(roleId, permId));
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedTenantResourcesAsync(AppDbContext db, DateTimeOffset now, ILogger logger, CancellationToken ct)
    {
        var tenants = await db.Tenants.IgnoreQueryFilters().ToListAsync(ct);
        var perms = await db.Permissions.ToListAsync(ct);
        var permLookup = perms.ToDictionary(p => p.Code, p => p.Id);

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

                if (!LegacyRolePermissions.TryGetValue(roleName, out var permCodes)) continue;
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

            var existingAgentCodes = await db.AgentConfigs
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenant.Id)
                .Select(a => a.Code)
                .ToListAsync(ct);
            foreach (var (code, displayName, agentType) in DefaultAgents)
            {
                if (existingAgentCodes.Contains(code)) continue;
                var agent = AgentConfig.Create(tenant.Id, code, displayName, agentType, "claude", now);
                agent.Start();
                db.AgentConfigs.Add(agent);
            }

            var hasWarmDrip = await db.Set<DripSequence>()
                .IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenant.Id && s.TriggerEvent == "warm_lead", ct);
            if (!hasWarmDrip)
            {
                var drip = DripSequence.Create(tenant.Id, "Warm lead nurture", "warm_lead", now);
                drip.AddStep(1, 1, "pancake", "Chao {lead_name}, cam on ban da quan tam toi Hoc Ba! Ban can tu van them ve khoa hoc nao?");
                drip.AddStep(2, 47, "pancake", "{lead_name} oi, Hoc Ba dang co uu dai hoc thu mien phi - ban co muon dat lich trai nghiem khong?");
                drip.AddStep(3, 72, "pancake", "Hoc Ba gui {lead_name} lo trinh hoc tieng Trung ca nhan hoa. Ban tham khao thu nhe!");
                drip.AddStep(4, 48, "pancake", "{lead_name} con ban khoan gi ve khoa hoc khong? Doi ngu Hoc Ba luon san sang ho tro ban.");
                db.Set<DripSequence>().Add(drip);
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
        Message = "RbacSeeder: {Count} role-permission links seeded")]
    private static partial void LogRolePermsSeeded(ILogger logger, int count);
}
