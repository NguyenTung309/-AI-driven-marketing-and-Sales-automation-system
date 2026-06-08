using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Security;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRoles(this IEndpointRouteBuilder app)
    {
        // SPEC-11 §6a: the whole RBAC editor requires rbac:manage (Admin only).
        var roles = app.MapGroup("/api/rbac/roles").RequirePermission("rbac:manage");

        roles.MapGet("/", ListRolesAsync);
        roles.MapPost("/", CreateRoleAsync);
        roles.MapPut("/{id:guid}", UpdateRoleAsync);
        roles.MapDelete("/{id:guid}", DeleteRoleAsync);
        roles.MapGet("/{id:guid}/permissions", ListRolePermissionsAsync);
        roles.MapPut("/{id:guid}/permissions", SetRolePermissionsAsync);

        var perms = app.MapGroup("/api/rbac/permissions").RequirePermission("rbac:manage");
        perms.MapGet("/", ListPermissionsAsync);

        return app;
    }

    private static async Task<IResult> ListRolesAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var list = await db.RbacRoles
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem))
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateRoleAsync(
        CreateRoleRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("name_required");
        var tenantId = tenants.Require().TenantId;

        var duplicate = await db.RbacRoles.AnyAsync(r => r.TenantId == tenantId && r.Name == req.Name, ct);
        if (duplicate) return Results.Conflict("role_exists");

        var role = Role.Create(tenantId, req.Name, req.Description, isSystem: false, clock.UtcNow);
        db.RbacRoles.Add(role);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/rbac/roles/{role.Id}",
            new RoleDto(role.Id, role.Name, role.Description, role.IsSystem));
    }

    private static async Task<IResult> UpdateRoleAsync(
        Guid id,
        UpdateRoleRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var role = await db.RbacRoles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (role is null) return Results.NotFound();
        if (role.IsSystem) return Results.Forbid();

        var entry = db.Entry(role);
        entry.Property("Name").CurrentValue = req.Name;
        entry.Property("Description").CurrentValue = req.Description;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new RoleDto(role.Id, role.Name, role.Description, role.IsSystem));
    }

    private static async Task<IResult> DeleteRoleAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var role = await db.RbacRoles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
        if (role is null) return Results.NotFound();
        if (role.IsSystem) return Results.Forbid();

        db.RbacRoles.Remove(role);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // SPEC-11 D7: read/write role_permissions keyed on the fixed Identity AppRole.Id (the
    // same store the backend resolves permissions from) — not the domain RbacRoles + tenant.
    private static async Task<IResult> ListRolePermissionsAsync(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        if (!RbacSeeder.RoleIds.Values.Contains(id)) return Results.NotFound();

        var perms = await (
            from rp in db.RolePermissions
            join p in db.Permissions on rp.PermissionId equals p.Id
            where rp.RoleId == id
            select new PermissionDto(p.Id, p.Code, p.Description)).ToListAsync(ct);
        return Results.Ok(perms);
    }

    private static async Task<IResult> SetRolePermissionsAsync(
        Guid id,
        SetRolePermissionsRequest req,
        AppDbContext db,
        IPermissionResolver permissions,
        CancellationToken ct)
    {
        if (!RbacSeeder.RoleIds.Values.Contains(id)) return Results.NotFound();

        var existing = db.RolePermissions.Where(rp => rp.RoleId == id);
        db.RolePermissions.RemoveRange(existing);

        var validIds = await db.Permissions
            .Where(p => req.PermissionIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var permissionId in validIds)
            db.RolePermissions.Add(RolePermission.Create(id, permissionId));

        await db.SaveChangesAsync(ct);
        // SPEC-11 D7: invalidate the cache so the permission change takes effect immediately.
        await permissions.InvalidateAsync(id, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListPermissionsAsync(AppDbContext db, CancellationToken ct)
    {
        var perms = await db.Permissions
            .OrderBy(p => p.Code)
            .Select(p => new PermissionDto(p.Id, p.Code, p.Description))
            .ToListAsync(ct);
        return Results.Ok(perms);
    }
}
