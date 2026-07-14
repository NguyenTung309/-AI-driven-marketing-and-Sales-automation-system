using System.Security.Claims;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Notifications;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Endpoints;

public sealed record NotificationPreferenceDto(string Type, bool InApp, bool Push, bool Email);

public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceDto> Items);

public sealed record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);

// Tuỳ chọn thông báo của chính user + đăng ký Web Push của trình duyệt. Không gate permission:
// đây là dữ liệu của bản thân người dùng, chỉ cần đăng nhập.
public static class NotificationPreferencesEndpoints
{
    // Các loại thông báo user chỉnh được. Cảnh báo lỗi (severity=warning) luôn đẩy, không nằm ở đây.
    private static readonly (string Type, string Label)[] Catalog =
    [
        ("job_succeeded", "Việc AI chạy xong"),
        ("orchestration_completed", "Phiên điều phối hoàn thành"),
        ("content_trend_scan", "Quét xu hướng nội dung"),
        ("ads_weekly_report", "Báo cáo quảng cáo tuần"),
        ("ads_daypart", "AI chỉnh quảng cáo theo khung giờ"),
        ("ads_creative_rotation", "AI xoay vòng creative"),
        ("ads_remarketing", "AI cập nhật tệp remarketing"),
        ("drip_sent", "AI gửi tin chăm sóc theo kịch bản"),
        ("comment_auto_reply", "AI trả lời bình luận"),
        ("contact_memory_learned", "AI ghi nhớ thông tin khách"),
        ("agent_memory_learned", "Agent rút ra bài học mới"),
    ];

    public static IEndpointRouteBuilder MapNotificationPreferences(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/notifications/preferences")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("", ListAsync);
        grp.MapPut("", UpdateAsync);

        var push = app.MapGroup("/api/push")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        push.MapGet("/vapid-public-key", GetVapidKey);
        push.MapPost("/subscribe", SubscribeAsync);
        push.MapDelete("/subscribe", UnsubscribeAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db, ITenantAccessor tenants, HttpContext http, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var userId = RequireUserId(http);
        if (userId is null) return Results.Unauthorized();

        var saved = await db.NotificationPreferences.AsNoTracking()
            .Where(p => p.TenantId == tenant.TenantId && p.UserId == userId)
            .ToDictionaryAsync(p => p.Type, ct).ConfigureAwait(false);

        var items = Catalog.Select(entry =>
        {
            var pref = saved.GetValueOrDefault(entry.Type);
            return new
            {
                type = entry.Type,
                label = entry.Label,
                inApp = pref?.InApp ?? true,
                push = pref?.Push ?? NotificationDeliveryPolicy.DefaultPush(entry.Type),
                email = pref?.Email ?? false,
            };
        }).ToList();

        return Results.Ok(new { items });
    }

    private static async Task<IResult> UpdateAsync(
        UpdateNotificationPreferencesRequest body,
        AppDbContext db, ITenantAccessor tenants, IClock clock, HttpContext http, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var userId = RequireUserId(http);
        if (userId is null) return Results.Unauthorized();
        if (body.Items is null || body.Items.Count == 0)
            return Results.BadRequest(new { error = "items_required" });

        var known = Catalog.Select(c => c.Type).ToHashSet(StringComparer.Ordinal);
        var existing = await db.NotificationPreferences
            .Where(p => p.TenantId == tenant.TenantId && p.UserId == userId)
            .ToDictionaryAsync(p => p.Type, ct).ConfigureAwait(false);

        foreach (var item in body.Items.Where(i => known.Contains(i.Type)))
        {
            if (existing.TryGetValue(item.Type, out var pref))
                pref.Update(item.InApp, item.Push, item.Email, clock.UtcNow);
            else
                db.NotificationPreferences.Add(NotificationPreference.Create(
                    tenant.TenantId, userId.Value, item.Type, item.InApp, item.Push, item.Email, clock.UtcNow));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static IResult GetVapidKey(IOptions<WebPushOptions> options) =>
        Results.Ok(new { publicKey = options.Value.IsConfigured ? options.Value.PublicKey : null });

    private static async Task<IResult> SubscribeAsync(
        PushSubscribeRequest body,
        AppDbContext db, ITenantAccessor tenants, IClock clock, HttpContext http, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var userId = RequireUserId(http);
        if (userId is null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(body.Endpoint) || string.IsNullOrWhiteSpace(body.P256dh) || string.IsNullOrWhiteSpace(body.Auth))
            return Results.BadRequest(new { error = "subscription_invalid" });

        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == body.Endpoint, ct).ConfigureAwait(false);
        if (existing is not null) return Results.NoContent(); // trình duyệt đăng ký lại cùng endpoint

        db.PushSubscriptions.Add(Clawbot.Domain.Notifications.PushSubscription.Create(
            tenant.TenantId, userId.Value, body.Endpoint.Trim(), body.P256dh.Trim(), body.Auth.Trim(), clock.UtcNow));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> UnsubscribeAsync(
        AppDbContext db, ITenantAccessor tenants, HttpContext http, CancellationToken ct)
    {
        _ = tenants.Require();
        var endpoint = http.Request.Query["endpoint"].ToString();
        if (string.IsNullOrWhiteSpace(endpoint)) return Results.BadRequest(new { error = "endpoint_required" });

        var subscription = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct).ConfigureAwait(false);
        if (subscription is not null)
        {
            db.PushSubscriptions.Remove(subscription);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    private static Guid? RequireUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : null;
}
