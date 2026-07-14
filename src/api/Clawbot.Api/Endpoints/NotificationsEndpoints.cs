using System.Security.Claims;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/notifications").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync);
        grp.MapGet("/unread-count", UnreadCountAsync);
        grp.MapPost("/{id:guid}/read", MarkReadAsync);
        grp.MapPost("/read-all", MarkAllReadAsync);

        return grp;
    }

    private static Guid? CurrentUser(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    // Visible = own notifications + tenant broadcasts (user_id null), trừ loại user đã tắt in-app.
    // Cảnh báo (warning trở lên) không lọc: tắt được là AI hỏng mà không ai biết.
    private static IQueryable<Notification> Visible(AppDbContext db, Guid tenantId, Guid? userId, IReadOnlyCollection<string> mutedTypes)
    {
        var query = db.Notifications.Where(n => n.TenantId == tenantId && (n.UserId == null || n.UserId == userId));
        if (mutedTypes.Count > 0)
            query = query.Where(n => n.Severity == "warning" || !mutedTypes.Contains(n.Type));
        return query;
    }

    private static async Task<IReadOnlyCollection<string>> MutedTypesAsync(
        AppDbContext db, Guid tenantId, Guid? userId, CancellationToken ct)
    {
        if (userId is null) return Array.Empty<string>();
        return await db.NotificationPreferences.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.UserId == userId && !p.InApp)
            .Select(p => p.Type)
            .ToListAsync(ct);
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        ClaimsPrincipal user,
        [FromQuery] bool? unread,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var userId = CurrentUser(user);
        var muted = await MutedTypesAsync(db, tenantId, userId, ct);
        var query = Visible(db, tenantId, userId, muted);
        if (unread == true) query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new { n.Id, n.Type, n.Severity, n.Title, n.Body, n.Link, n.IsRead, n.ReadAt, n.CreatedAt, n.OccurrenceCount })
            .ToListAsync(ct);

        return Results.Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> UnreadCountAsync(
        AppDbContext db, ITenantAccessor tenants, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var userId = CurrentUser(user);
        var muted = await MutedTypesAsync(db, tenantId, userId, ct);
        var count = await Visible(db, tenantId, userId, muted).CountAsync(n => !n.IsRead, ct);
        return Results.Ok(new { count });
    }

    private static async Task<IResult> MarkReadAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var notification = await Visible(db, tenantId, CurrentUser(user), Array.Empty<string>()).FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notification is null) return Results.NotFound();

        notification.MarkRead(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllReadAsync(
        AppDbContext db, ITenantAccessor tenants, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var unread = await Visible(db, tenantId, CurrentUser(user), Array.Empty<string>()).Where(n => !n.IsRead).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var n in unread) n.MarkRead(now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { updated = unread.Count });
    }
}
