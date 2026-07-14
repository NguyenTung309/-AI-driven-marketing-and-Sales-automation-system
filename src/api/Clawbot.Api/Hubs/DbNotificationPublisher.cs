using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Notifications;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Hangfire;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

/// <summary>Persists the notification then pushes it realtime (per-user group, or tenant group for broadcast).</summary>
public sealed class DbNotificationPublisher(
    AppDbContext db,
    IHubContext<NotificationHub> hub,
    IBackgroundJobClient jobs) : INotificationPublisher
{
    public async Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var notification = await NotificationGrouping
            .UpsertAsync(db, request, DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);

        var payload = new
        {
            notification.Id,
            notification.Type,
            notification.Severity,
            notification.Title,
            notification.Body,
            notification.Link,
            notification.CreatedAt,
            notification.OccurrenceCount,
            // FE chỉ nổi toast khi push=true; feed vẫn nhận mọi thứ (nhóm máy móc mặc định không rung chuông).
            Push = NotificationDeliveryPolicy.IsAlwaysPushed(notification.Severity)
                || NotificationDeliveryPolicy.DefaultPush(notification.Type),
        };
        var target = request.UserId is { } userId
            ? hub.Clients.Group(NotificationHub.UserGroup(userId))
            : hub.Clients.Group(NotificationHub.TenantGroup(request.TenantId));
        await target.SendAsync("notification", payload, ct).ConfigureAwait(false);

        // Đóng tab thì SignalR không tới được: đẩy Web Push nền (tự thoát nếu chưa cấu hình VAPID).
        jobs.Enqueue<WebPushDispatchJob>(j => j.SendAsync(notification.Id, CancellationToken.None));
    }
}
