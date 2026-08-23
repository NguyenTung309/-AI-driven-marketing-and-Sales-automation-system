using System.Text.Json.Nodes;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Auth;
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
        // Sale chi thay lead cua kenh Pancake minh phu trach; cac role con lai xem toan bo
        // lead cua tenant (LeadScopeResolver doc quyen nay de quyet dinh pham vi).
        ("leads:read:all", [Admin, SalesLead, Marketer, QA, Viewer]),
        ("leads:write", [Admin, SalesLead, Sale]),
        ("content:read", All),
        ("content:write", [Admin, Marketer]),
        // Phase 4.8: human publishing decisions vs privileged external delivery separation.
        // Marketer may approve/reject; only Admin may retry/reconcile publish attempts.
        ("content:approve", [Admin, Marketer]),
        ("content:publish", [Admin]),
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
        ("llm-configs:manage", [Admin]),
        ("orchestration:view", [Admin, SalesLead, Marketer]),
        ("orchestration:run", [Admin, SalesLead, Marketer]),
        ("orchestration:approve", [Admin, SalesLead]),
        ("orchestration:manage", [Admin]),
        ("jobs:view", All),
        ("jobs:manage", [Admin, SalesLead, Marketer]),
        ("rbac:manage", [Admin]),
        ("users:manage", [Admin]),
        ("users:pancake-token:manage", [Admin, SalesLead]),
        ("system:config", [Admin]),
        ("system.logs", [Admin]),
        ("admin:inboxes", [Admin]),
        ("admin:sale-manage", []),
        ("admin:integration", [Admin]),
        ("admin:jobs-hangfires", [Admin]),
    ];

    // RBAC_Redesign: ads đã bị gỡ khỏi ma trận phân quyền của mọi role (kể cả Admin).
    // admin.system đổi tên thành admin:users-manage.
    // Seeder vốn chỉ biết thêm nên phải dọn cả link + permission đã seed ở DB cũ.
    // List<T>, khong phai string[]: EF Core dung interpreter (Expression.Compile(preferInterpretation: true))
    // de evaluate closure nay truoc khi parameterize query, va tren .NET 8.0.29 hien tai viec goi
    // Contains() tren mot array field qua closure lam interpreter crash (TypeLoadException voi
    // ReadOnlySpan<string> generic argument). List<T>.Contains() khong di qua code path do.
    private static readonly List<string> DeprecatedPermissions = ["ads:read", "ads:write", "ads.read", "ads.manage", "admin.system"];

    private static readonly (string Code, string DisplayName, string AgentType)[] DefaultAgents =
    [
        ("orchestrator", "Điều phối viên", "planner"),
        ("chat-agent", "Agent-Chat", "chat"),
        ("sale-assist", "Agent-SaleAssist", "sale_assist"),
        ("lead-agent", "Agent-Lead", "lead"),
        ("content-agent", "Agent-Content", "content"),
        ("research-agent", "Agent-Research", "research"),
        ("docs-agent", "Agent-Docs", "docs"),
        ("report-agent", "Agent-Report", "report"),
        // Review-gate Phase 0: reviewer needs an AgentConfig row so it auto-binds to the tenant's LLM
        // config here (like every other agent) — AgentDefinitionCatalog hides unbound agents, which made
        // the reviewer invisible in prod (dev worked only via DemoLlmConfigSeeder).
        ("reviewer-agent", "Agent-Review", "reviewer"),
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
            "analytics.read",
            "admin:users-manage", "admin.audit", "system.logs",
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
        await SeedRolePermissionsAsync(db, sp, ct);
        await RemoveDeprecatedPermissionsAsync(db, sp, logger, ct);
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

    private static async Task SeedRolePermissionsAsync(AppDbContext db, IServiceProvider sp, CancellationToken ct)
    {
        var permIdByCode = await db.Permissions.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var have = (await db.RolePermissions.Select(rp => new { rp.RoleId, rp.PermissionId }).ToListAsync(ct))
            .Select(x => (x.RoleId, x.PermissionId)).ToHashSet();
        var granted = new HashSet<Guid>();

        foreach (var (code, roles) in Matrix)
        {
            if (!permIdByCode.TryGetValue(code, out var permId)) continue;
            foreach (var role in roles)
            {
                var roleId = RoleIds[role];
                // Cập nhật have sau Add — Matrix + Legacy có thể trùng (role,perm).
                if (!have.Add((roleId, permId))) continue;
                db.RolePermissions.Add(RolePermission.Create(roleId, permId));
                granted.Add(roleId);
            }
        }

        foreach (var (role, codes) in LegacyRolePermissions)
        {
            var roleId = RoleIds[role];
            foreach (var code in codes)
            {
                if (!permIdByCode.TryGetValue(code, out var permId)) continue;
                if (!have.Add((roleId, permId))) continue;
                db.RolePermissions.Add(RolePermission.Create(roleId, permId));
                granted.Add(roleId);
            }
        }

        await db.SaveChangesAsync(ct);

        // Quyền mới seed phải hiệu lực ngay: PermissionResolver cache Redis 10' nên nếu không
        // invalidate, role vừa được cấp quyền vẫn bị từ chối hết TTL sau khi deploy.
        if (granted.Count == 0) return;
        var resolver = sp.GetRequiredService<IPermissionResolver>();
        foreach (var roleId in granted)
            await resolver.InvalidateAsync(roleId, ct);
    }

    /// <summary>
    /// Idempotently drops deprecated (ads) permissions: role links + permission rows, rồi
    /// invalidate cache Redis của các role bị ảnh hưởng để quyền cũ không sống thêm hết TTL.
    /// </summary>
    private static async Task RemoveDeprecatedPermissionsAsync(AppDbContext db, IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        var perms = await db.Permissions
            .Where(p => DeprecatedPermissions.Contains(p.Code))
            .ToListAsync(ct);
        if (perms.Count == 0) return;

        // List<T> khong phai array: xem ghi chu o DeprecatedPermissions ve interpreter crash tren Contains().
        var permIds = perms.Select(p => p.Id).ToList();
        var links = await db.RolePermissions
            .Where(rp => permIds.Contains(rp.PermissionId))
            .ToListAsync(ct);

        db.RolePermissions.RemoveRange(links);
        db.Permissions.RemoveRange(perms);
        await db.SaveChangesAsync(ct);

        var resolver = sp.GetRequiredService<IPermissionResolver>();
        foreach (var roleId in links.Select(l => l.RoleId).Distinct())
            await resolver.InvalidateAsync(roleId, ct);

        LogDeprecatedPermsRemoved(logger, perms.Count, links.Count);
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

            var activeLlmConfigs = await db.LlmConfigs
                .IgnoreQueryFilters()
                .Where(config => config.TenantId == tenant.Id && config.IsActive)
                .OrderBy(config => config.CreatedAt)
                .Select(config => new { Id = (Guid?)config.Id, config.ModelId })
                .ToListAsync(ct);
            var defaultLlmConfig = activeLlmConfigs.FirstOrDefault();
            var modelByConfigId = activeLlmConfigs
                .Where(config => config.Id.HasValue)
                .ToDictionary(config => config.Id!.Value, config => config.ModelId);
            var existingAgents = await db.AgentConfigs
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenant.Id)
                .ToListAsync(ct);
            foreach (var (code, displayName, agentType) in DefaultAgents)
            {
                var agent = existingAgents.FirstOrDefault(existing => existing.Code == code);
                if (agent is null)
                {
                    agent = AgentConfig.Create(tenant.Id, code, displayName, agentType, defaultLlmConfig?.ModelId ?? string.Empty, now);
                    agent.Start();
                    db.AgentConfigs.Add(agent);
                    existingAgents.Add(agent);
                }

                var effectiveModel = string.Equals(agent.Model, "claude", StringComparison.OrdinalIgnoreCase)
                    ? ResolveSeededModel(agent.LlmConfigId, modelByConfigId, defaultLlmConfig?.ModelId, agent.Model)
                    : agent.Model;
                agent.UpdateSettings(displayName, effectiveModel, agent.SkillFilesJson, agent.KbModulesJson, MergeOrchestrationConfig(agent.ConfigJson, code), now);
                if (agent.LlmConfigId is null && defaultLlmConfig?.Id is { } llmConfigId)
                    agent.BindLlmConfig(llmConfigId, now);
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

    private static string ResolveSeededModel(
        Guid? llmConfigId,
        Dictionary<Guid, string> modelByConfigId,
        string? defaultModel,
        string currentModel)
    {
        if (llmConfigId.HasValue && modelByConfigId.TryGetValue(llmConfigId.Value, out var boundModel))
            return boundModel;

        return string.IsNullOrWhiteSpace(defaultModel) ? currentModel : defaultModel;
    }

    private static string MergeOrchestrationConfig(string configJson, string code)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(configJson)
                ? new JsonObject()
                : JsonNode.Parse(configJson)?.AsObject() ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            root = new JsonObject();
        }

        var metadata = JsonNode.Parse(BuildOrchestrationConfig(code))!.AsObject()["orchestration"]!.DeepClone();
        root["orchestration"] = metadata;

        // Upgrade only a blank, explicitly versioned default, or the exact legacy default. A prompt changed in
        // /api/prompts/configs has no seed version and therefore remains a tenant customization.
        var existingPrompt = root["systemPrompt"]?.GetValue<string>();
        var promptVersion = root["systemPromptVersion"] is JsonValue versionNode
            && versionNode.TryGetValue<int>(out var parsedVersion)
            ? parsedVersion
            : (int?)null;
        if (Clawbot.Agents.Core.AgentPromptPacks.ShouldRefreshSeededPrompt(code, existingPrompt, promptVersion))
        {
            root["systemPrompt"] = Clawbot.Agents.Core.AgentPromptPacks.For(code);
            root["systemPromptVersion"] = Clawbot.Agents.Core.AgentPromptPacks.PromptPackVersion;
        }

        return root.ToJsonString();
    }

    private static string BuildOrchestrationConfig(string code) => code switch
    {
        "orchestrator" => "{\"orchestration\":{\"description\":\"Lập kế hoạch và điều phối các tác vụ đa tác nhân.\",\"inputSchema\":\"{\\\"goal\\\":\\\"string\\\"}\",\"orchestratable\":false}}",
        "chat-agent" => "{\"orchestration\":{\"description\":\"Draft a non-streaming customer chat reply.\",\"inputSchema\":\"{\\\"tenant_id\\\":\\\"guid\\\",\\\"user_text\\\":\\\"string\\\"}\",\"orchestratable\":true}}",
        "sale-assist" => "{\"orchestration\":{\"description\":\"Summarize, draft, or suggest upsells for sales conversations.\",\"inputSchema\":\"{\\\"tenant_id\\\":\\\"guid\\\",\\\"conversation_id\\\":\\\"guid\\\",\\\"turns_json\\\":\\\"array\\\"}\",\"orchestratable\":true}}",
        "lead-agent" => "{\"orchestration\":{\"description\":\"Score or create lead records from campaign context.\",\"inputSchema\":\"{\\\"tenant_id\\\":\\\"guid\\\",\\\"operation\\\":\\\"score|create\\\"}\",\"orchestratable\":true}}",
        "content-agent" => "{\"orchestration\":{\"description\":\"Generate platform-specific campaign content from a brief.\",\"inputSchema\":\"{\\\"tenant_id\\\":\\\"guid\\\",\\\"platform\\\":\\\"string\\\",\\\"brief\\\":\\\"string\\\"}\",\"orchestratable\":true}}",
        "research-agent" => "{\"orchestration\":{\"description\":\"Research markets, competitors, and keyword topics.\",\"inputSchema\":\"{\\\"tenant_id\\\":\\\"guid\\\",\\\"geo\\\":\\\"string\\\",\\\"keywords\\\":\\\"array\\\"}\",\"orchestratable\":true}}",
        "docs-agent" => "{\"orchestration\":{\"description\":\"Render templated documents with tenant branding.\",\"inputSchema\":\"{\\\"tenant_id\\\":\\\"guid\\\",\\\"template_code\\\":\\\"string\\\",\\\"template_body\\\":\\\"string\\\"}\",\"orchestratable\":true}}",
        "report-agent" => "{\"orchestration\":{\"description\":\"Build tenant analytics and performance reports.\",\"inputSchema\":\"{\\\"tenant_id\\\":\\\"guid\\\",\\\"report_type\\\":\\\"string\\\"}\",\"orchestratable\":true}}",
        _ => "{\"orchestration\":{\"description\":\"Run agent task.\",\"inputSchema\":\"{}\",\"orchestratable\":true}}",
    };

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

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "RbacSeeder: removed {PermCount} deprecated permissions and {LinkCount} role links")]
    private static partial void LogDeprecatedPermsRemoved(ILogger logger, int permCount, int linkCount);
}
