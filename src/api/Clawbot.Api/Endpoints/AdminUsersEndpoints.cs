using System.Security.Claims;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// PancakePageId + PancakeAccessToken: cau hinh kenh cua sale ngay tren form user - luu ve inbox
// (page_id + token per kenh, nguon duy nhat cho polling/outbound) va gan user lam member cua kenh.
public sealed record CreateUserRequest(string Email, string DisplayName, string Password, string[]? Roles, string? PancakeAccessToken, string? PancakePageId, string? PancakePlatform, string? PancakeChannelName);
public sealed record UpdateUserRequest(string? DisplayName, string[]? Roles, bool? IsActive, string? PancakeAccessToken, bool? ClearPancakeAccessToken, string? PancakePageId, string? PancakePlatform, string? PancakeChannelName);

public sealed record AdminUserListItem(
    Guid Id,
    string? Email,
    string DisplayName,
    string? Phone,
    bool IsActive,
    DateTimeOffset? LastLoginAt,
    bool HasPancakeAccessToken,
    IReadOnlyList<string>? Roles,
    IReadOnlyList<AdminUserPancakeChannel> PancakeChannels);

public sealed record AdminUserPancakeChannel(
    Guid InboxId,
    string PageId,
    string Name,
    string Platform,
    bool HasToken);

public sealed record AdminUsersPage(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<AdminUserListItem> Items);

// M23 — admin user management (permission: admin.system). Operates on Identity AppUser (`users` table).
public static class AdminUsersEndpoints
{
    public static IEndpointRouteBuilder MapAdminUsers(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin/users")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync);
        grp.MapPost("/", CreateAsync);
        grp.MapPut("/{id:guid}", UpdateAsync);
        grp.MapPost("/{id:guid}/disable", DisableAsync).RequirePermission("admin.system");
        grp.MapPost("/{id:guid}/enable", EnableAsync).RequirePermission("admin.system");
        grp.MapPost("/{id:guid}/reset-password", ResetPasswordAsync).RequirePermission("admin.system");

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
        UserManager<AppUser> users,
        ITenantAccessor tenants,
        AppDbContext db,
        ClaimsPrincipal principal,
        IPermissionResolver permissions,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!await HasAnyPermissionAsync(principal, permissions, ct, "admin.system", "users:pancake-token:manage"))
            return Results.Forbid();

        // Role assignments disclose broader authorization metadata, so only system administrators receive them.
        var includeRoles = await HasPermissionAsync(principal, permissions, "admin.system", ct);
        var usersPage = await QueryUsersAsync(
            db,
            tenants.Require().TenantId,
            q,
            page,
            pageSize,
            includeRoles,
            ct);

        return Results.Ok(usersPage);
    }

    internal static async Task<AdminUsersPage> QueryUsersAsync(
        AppDbContext db,
        Guid tenantId,
        string? queryText,
        int page,
        int pageSize,
        bool includeRoles,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Users.Where(u => u.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(queryText))
            query = query.Where(u => u.Email!.Contains(queryText) || u.DisplayName.Contains(queryText));

        var total = await query.CountAsync(ct);
        var pageQuery = query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
        var rows = await pageQuery
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                Phone = u.PhoneNumber,
                u.IsActive,
                u.LastLoginAt,
                HasPancakeAccessToken = u.PancakeAccessTokenEncrypted != null,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new AdminUsersPage(total, page, pageSize, []);

        // Lọc kênh/role theo đúng tập user của trang bằng IN (subquery) thay vì array.Contains:
        // EF 8 dịch Guid[].Contains thành primitive collection và ném TypeLoadException
        // ReadOnlySpan trong expression interpreter khi chạy trên runtime .NET 10 (CI).
        var pageUserIds = pageQuery.Select(u => u.Id);

        var channels = await db.InboxMembers
            .IgnoreQueryFilters()
            .Where(member => member.TenantId == tenantId && pageUserIds.Contains(member.AgentId))
            .Join(db.Inboxes.IgnoreQueryFilters().Where(inbox => inbox.TenantId == tenantId && inbox.DeletedAt == null),
                member => member.InboxId,
                inbox => inbox.Id,
                (member, inbox) => new
                {
                    member.AgentId,
                    InboxId = inbox.Id,
                    PageId = inbox.ExternalPageId,
                    inbox.Name,
                    inbox.Platform,
                    HasToken = inbox.EncryptedAccessToken != null,
                })
            .ToListAsync(ct);

        var rolesByUserId = includeRoles
            ? (await db.UserRoles
                .Join(db.Roles,
                    userRole => userRole.RoleId,
                    role => role.Id,
                    (userRole, role) => new { userRole.UserId, role.Name })
                .Where(row => pageUserIds.Contains(row.UserId) && row.Name != null)
                .ToListAsync(ct))
                .GroupBy(row => row.UserId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(row => row.Name!)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToArray())
            : new Dictionary<Guid, IReadOnlyList<string>>();

        var items = rows.Select(user => new AdminUserListItem(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Phone,
            user.IsActive,
            user.LastLoginAt,
            user.HasPancakeAccessToken,
            includeRoles ? rolesByUserId.GetValueOrDefault(user.Id, Array.Empty<string>()) : null,
            channels
                .Where(channel => channel.AgentId == user.Id)
                .Select(channel => new AdminUserPancakeChannel(
                    channel.InboxId,
                    channel.PageId,
                    channel.Name,
                    channel.Platform,
                    channel.HasToken))
                .ToArray())).ToArray();

        return new AdminUsersPage(total, page, pageSize, items);
    }

    private static async Task<IResult> CreateAsync(
        CreateUserRequest req,
        UserManager<AppUser> users,
        ITenantAccessor tenants,
        AppDbContext db,
        IPancakePageTokenService pageTokens,
        IEncryptor encryptor,
        IClock clock,
        ClaimsPrincipal principal,
        IPermissionResolver permissions,
        CancellationToken ct)
    {
        if (!await HasPermissionAsync(principal, permissions, "admin.system", ct))
            return Results.Forbid();

        var tenantId = tenants.Require().TenantId;
        var canManageToken = await HasPermissionAsync(principal, permissions, "users:pancake-token:manage", ct);
        if ((!string.IsNullOrWhiteSpace(req.PancakeAccessToken) || !string.IsNullOrWhiteSpace(req.PancakePageId)) && !canManageToken)
            return Results.Forbid();

        // Chan truoc khi ghi: neu de den ConnectPancakePageAsync moi bao inbox_not_found thi user +
        // role da commit, caller nhan 400 nhung du lieu van doi (va lan thu lai bao trung email).
        var pageError = await ValidatePancakePageAsync(db, tenantId, req.PancakePageId, req.PancakePlatform, req.PancakeAccessToken, ct);
        if (pageError is not null) return pageError;

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = req.Email,
            UserName = req.Email,
            DisplayName = req.DisplayName,
            IsActive = true,
        };

        // Token + page_id di theo kenh (inbox); chi con token khong kem page_id thi giu duong cu (cot user)
        if (!string.IsNullOrWhiteSpace(req.PancakeAccessToken) && string.IsNullOrWhiteSpace(req.PancakePageId))
        {
            user.PancakeAccessTokenEncrypted = encryptor.Encrypt(req.PancakeAccessToken.Trim());
            user.PancakeAccessTokenUpdatedAt = clock.UtcNow;
        }

        var created = await users.CreateAsync(user, req.Password);
        if (!created.Succeeded) return Results.BadRequest(created.Errors.Select(e => e.Description));

        if (req.Roles is { Length: > 0 })
            await users.AddToRolesAsync(user, req.Roles);

        if (!string.IsNullOrWhiteSpace(req.PancakePageId))
        {
            var err = await ConnectPancakePageAsync(db, pageTokens, tenantId, user.Id, req.PancakePageId, req.PancakePlatform, req.PancakeAccessToken, req.PancakeChannelName, clock.UtcNow, ct);
            if (err is not null) return err;
        }

        return Results.Created($"/api/admin/users/{user.Id}", new { user.Id, user.Email, user.DisplayName });
    }

    // Inbox mac dinh la zalo khi form khong chon nen tang — dung chung cho ca buoc kiem tra truoc va buoc ghi.
    private static string NormalizePlatform(string? platform) =>
        string.IsNullOrWhiteSpace(platform) ? "zalo" : platform.Trim().ToLowerInvariant();

    private static IResult InboxNotFound() =>
        Results.BadRequest(new { error = "inbox_not_found", message = "Kênh chưa tồn tại - nhập kèm Pancake Page Access Token để tạo kênh." });

    // Kiem tra page_id truoc khi doi user. Chi truong hop "co page_id, khong co token" moi hong duoc:
    // co token thi StorePageTokenDirectAsync tu tao/cap nhat inbox nen tra cuu sau do chac chan thay.
    private static async Task<IResult?> ValidatePancakePageAsync(
        AppDbContext db,
        Guid tenantId,
        string? pageId,
        string? platform,
        string? pageAccessToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pageId)) return null;
        if (!string.IsNullOrWhiteSpace(pageAccessToken)) return null;

        var plat = NormalizePlatform(platform);
        var trimmed = pageId.Trim();
        var exists = await db.Inboxes
            .IgnoreQueryFilters()
            .AnyAsync(i => i.TenantId == tenantId
                && i.Platform == plat
                && i.ExternalPageId == trimmed
                && i.DeletedAt == null, ct);

        return exists ? null : InboxNotFound();
    }

    // Cau hinh kenh tu form user: upsert inbox (page_id + token encrypted) roi gan user lam member.
    // Polling inbound + gui outbound deu doc token tu inbox nay.
    private static async Task<IResult?> ConnectPancakePageAsync(
        AppDbContext db,
        IPancakePageTokenService pageTokens,
        Guid tenantId,
        Guid userId,
        string pageId,
        string? platform,
        string? pageAccessToken,
        string? channelName,
        DateTimeOffset now,
        CancellationToken ct)
    {
        pageId = pageId.Trim();
        var plat = NormalizePlatform(platform);
        var name = channelName?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(pageAccessToken))
        {
            // name rong: giu ten inbox hien co, inbox moi fallback ve pageId
            await pageTokens.StorePageTokenDirectAsync(tenantId, pageId, name, plat, pageAccessToken.Trim(), ct);
        }

        var inbox = await db.Inboxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId
                && i.Platform == plat
                && i.ExternalPageId == pageId
                && i.DeletedAt == null, ct);
        if (inbox is null)
            return InboxNotFound();

        // Doi ten hien thi khong can nhap lai token
        if (name.Length > 0 && inbox.Name != name)
        {
            inbox.UpdateName(name, now);
            await db.SaveChangesAsync(ct);
        }

        var isMember = await db.InboxMembers
            .IgnoreQueryFilters()
            .AnyAsync(m => m.InboxId == inbox.Id && m.AgentId == userId, ct);
        if (!isMember)
        {
            db.InboxMembers.Add(InboxMember.Create(tenantId, inbox.Id, userId));
            await db.SaveChangesAsync(ct);
        }

        return null;
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateUserRequest req,
        UserManager<AppUser> users,
        ITenantAccessor tenants,
        AppDbContext db,
        IPancakePageTokenService pageTokens,
        IEncryptor encryptor,
        IClock clock,
        ClaimsPrincipal principal,
        IPermissionResolver permissions,
        CancellationToken ct)
    {
        var user = await FindInTenantAsync(users, tenants, id);
        if (user is null) return Results.NotFound();

        var hasUserChange = req.DisplayName is not null || req.IsActive is not null || req.Roles is not null;
        if (hasUserChange && !await HasPermissionAsync(principal, permissions, "admin.system", ct))
            return Results.Forbid();

        var hasTokenChange = !string.IsNullOrWhiteSpace(req.PancakeAccessToken)
            || !string.IsNullOrWhiteSpace(req.PancakePageId)
            || req.ClearPancakeAccessToken == true;
        if (hasTokenChange && !await HasPermissionAsync(principal, permissions, "users:pancake-token:manage", ct))
            return Results.Forbid();

        if (!hasUserChange && !hasTokenChange) return Results.NoContent();

        // Nhu CreateAsync: role/lockout commit ngay qua UserManager, nen phai chan page_id sai truoc khi doi user.
        var pageError = await ValidatePancakePageAsync(db, tenants.Require().TenantId, req.PancakePageId, req.PancakePlatform, req.PancakeAccessToken, ct);
        if (pageError is not null) return pageError;

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

        if (req.ClearPancakeAccessToken == true)
        {
            user.PancakeAccessTokenEncrypted = null;
            user.PancakeAccessTokenUpdatedAt = null;
        }
        else if (!string.IsNullOrWhiteSpace(req.PancakeAccessToken) && string.IsNullOrWhiteSpace(req.PancakePageId))
        {
            // Token khong kem page_id: giu duong cu (cot user)
            user.PancakeAccessTokenEncrypted = encryptor.Encrypt(req.PancakeAccessToken.Trim());
            user.PancakeAccessTokenUpdatedAt = clock.UtcNow;
        }

        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return Results.BadRequest(result.Errors.Select(e => e.Description));

        if (!string.IsNullOrWhiteSpace(req.PancakePageId))
        {
            var err = await ConnectPancakePageAsync(db, pageTokens, tenants.Require().TenantId, user.Id, req.PancakePageId, req.PancakePlatform, req.PancakeAccessToken, req.PancakeChannelName, clock.UtcNow, ct);
            if (err is not null) return err;
        }

        return Results.NoContent();
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

    private static async Task<bool> HasAnyPermissionAsync(
        ClaimsPrincipal principal,
        IPermissionResolver permissions,
        CancellationToken ct,
        params string[] codes)
    {
        foreach (var code in codes)
        {
            if (await HasPermissionAsync(principal, permissions, code, ct)) return true;
        }

        return false;
    }

    private static async Task<bool> HasPermissionAsync(
        ClaimsPrincipal principal,
        IPermissionResolver permissions,
        string code,
        CancellationToken ct)
    {
        if (principal.HasClaim("perm", code)) return true;
        if (!Guid.TryParse(principal.FindFirst("role_id")?.Value, out var roleId) || roleId == Guid.Empty)
            return false;

        var resolved = await permissions.GetPermissionsAsync(roleId, ct);
        return resolved.Contains(code);
    }
}
