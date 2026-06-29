using System.Security.Claims;
using Clawbot.Agents.Core.Docs;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record UpdateProfileRequest(string? DisplayName, string? Phone, DateOnly? DateOfBirth);

// M23 — current user's own profile (read/update). Avatar upload deferred (needs IDocumentStorage in API).
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfile(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/profile").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", GetAsync);
        grp.MapPut("/", UpdateAsync);
        grp.MapPost("/avatar", UploadAvatarAsync).DisableAntiforgery();
        grp.MapGet("/login-history", LoginHistoryAsync);

        return grp;
    }

    private static async Task<IResult> GetAsync(UserManager<AppUser> users, ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        var roles = await users.GetRolesAsync(user);
        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            Phone = user.PhoneNumber,
            user.DateOfBirth,
            user.AvatarUrl,
            user.IsActive,
            roles,
            tenantSlug = principal.FindFirstValue("tenant_slug"),
        });
    }

    private static async Task<IResult> UpdateAsync(
        UpdateProfileRequest req, UserManager<AppUser> users, ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        if (req.DisplayName is not null) user.DisplayName = req.DisplayName;
        if (req.Phone is not null) user.PhoneNumber = req.Phone;
        if (req.DateOfBirth is not null) user.DateOfBirth = req.DateOfBirth;

        var result = await users.UpdateAsync(user);
        return result.Succeeded
            ? Results.Ok(new { user.Id, user.DisplayName, Phone = user.PhoneNumber, user.DateOfBirth })
            : Results.BadRequest(result.Errors.Select(e => e.Description));
    }

    private static async Task<IResult> UploadAvatarAsync(
        IFormFile file,
        UserManager<AppUser> users,
        ClaimsPrincipal principal,
        [FromServices] IDocumentStorage storage,
        CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();
        if (file is null || file.Length == 0) return Results.BadRequest("Thiếu file ảnh.");
        if (file.Length > 2 * 1024 * 1024) return Results.BadRequest("Ảnh tối đa 2MB.");
        if (!(file.ContentType ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Chỉ chấp nhận file ảnh.");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var url = await storage.SaveAsync(ms.ToArray(), $"avatar-{user.Id}{ext}", file.ContentType, ct);

        user.AvatarUrl = url;
        await users.UpdateAsync(user);
        return Results.Ok(new { avatarUrl = url });
    }

    private static async Task<IResult> LoginHistoryAsync(
        UserManager<AppUser> users,
        ClaimsPrincipal principal,
        AppDbContext db,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.AuditLogs
            .Where(log => log.TenantId == user.TenantId && log.UserId == user.Id && log.Action == "auth.login");

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(log => log.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(log => new
        {
            log.Id,
            log.OccurredAt,
            ipAddress = log.IpAddress?.ToString(),
            userAgent = log.UserAgent,
            status = "success",
        });

        return Results.Ok(new { total, page, pageSize, items });
    }
}
