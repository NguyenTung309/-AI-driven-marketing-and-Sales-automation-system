using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Application.Abstractions;
using Clawbot.Infrastructure.Identity;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace Clawbot.Api.Endpoints;

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, string[]? Roles);
public sealed record UpdateUserRequest(string? DisplayName, string[]? Roles, bool? IsActive);

// M23 — admin user management (perm: admin.system). Operates on Identity AppUser (`users` table).
public static class AdminUsersEndpoints
{
    public static IEndpointRouteBuilder MapAdminUsers(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin/users")
            .RequirePermission("admin.system")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync);
        grp.MapPost("/", CreateAsync);
        grp.MapPut("/{id:guid}", UpdateAsync);
        grp.MapPost("/{id:guid}/disable", DisableAsync);
        grp.MapPost("/{id:guid}/enable", EnableAsync);
        grp.MapPost("/{id:guid}/reset-password", ResetPasswordAsync);

        return grp;
    }

    // Identity AppUser is not ITenantOwned (no global filter), so admin ops must scope by tenant
    // explicitly — otherwise an admin could mutate another tenant's user by guessing the id (IDOR).
    private static async Task<AppUser?> FindInTenantAsync(UserManager<AppUser> users, ITenantAccessor tenants, Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is not null && user.TenantId == tenants.Require().TenantId ? user : null;
    }

    private static async Task<IResult> ListAsync(
        UserManager<AppUser> users, ITenantAccessor tenants,
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = users.Users.Where(u => u.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.Email!.Contains(q) || u.DisplayName.Contains(q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new { u.Id, u.Email, u.DisplayName, Phone = u.PhoneNumber, u.IsActive, u.LastLoginAt })
            .ToListAsync(ct);

        return Results.Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> CreateAsync(
        CreateUserRequest req, UserManager<AppUser> users, ITenantAccessor tenants)
    {
        var tenantId = tenants.Require().TenantId;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = req.Email,
            UserName = req.Email,
            DisplayName = req.DisplayName,
            IsActive = true,
        };

        var created = await users.CreateAsync(user, req.Password);
        if (!created.Succeeded) return Results.BadRequest(created.Errors.Select(e => e.Description));

        if (req.Roles is { Length: > 0 })
            await users.AddToRolesAsync(user, req.Roles);

        return Results.Created($"/api/admin/users/{user.Id}", new { user.Id, user.Email, user.DisplayName });
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateUserRequest req, UserManager<AppUser> users, ITenantAccessor tenants)
    {
        var user = await FindInTenantAsync(users, tenants, id);
        if (user is null) return Results.NotFound();

        if (req.DisplayName is not null) user.DisplayName = req.DisplayName;
        if (req.IsActive is { } active)
        {
            user.IsActive = active;
            await users.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);
        }

        if (req.Roles is not null)
        {
            var current = await users.GetRolesAsync(user);
            await users.RemoveFromRolesAsync(user, current.Except(req.Roles));
            await users.AddToRolesAsync(user, req.Roles.Except(current));
        }

        var result = await users.UpdateAsync(user);
        return result.Succeeded ? Results.NoContent() : Results.BadRequest(result.Errors.Select(e => e.Description));
    }

    private static Task<IResult> DisableAsync(Guid id, UserManager<AppUser> users, ITenantAccessor tenants) => SetActiveAsync(id, false, users, tenants);
    private static Task<IResult> EnableAsync(Guid id, UserManager<AppUser> users, ITenantAccessor tenants) => SetActiveAsync(id, true, users, tenants);

    private static async Task<IResult> SetActiveAsync(Guid id, bool active, UserManager<AppUser> users, ITenantAccessor tenants)
    {
        var user = await FindInTenantAsync(users, tenants, id);
        if (user is null) return Results.NotFound();
        user.IsActive = active;
        await users.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);
        await users.UpdateAsync(user);
        return Results.Ok(new { user.Id, user.IsActive });
    }

    private static async Task<IResult> ResetPasswordAsync(Guid id, UserManager<AppUser> users, IEmailSender email, ITenantAccessor tenants)
    {
        var user = await FindInTenantAsync(users, tenants, id);
        if (user is null) return Results.NotFound();

        var token = await users.GeneratePasswordResetTokenAsync(user);
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            await email.SendAsync(user.Email, "Đặt lại mật khẩu Học Bá Admin",
                $"Quản trị viên đã yêu cầu đặt lại mật khẩu. Mã đặt lại: {token}");
        }
        return Results.Ok(new { message = "Reset token issued (emailed if SMTP configured)." });
    }
}
