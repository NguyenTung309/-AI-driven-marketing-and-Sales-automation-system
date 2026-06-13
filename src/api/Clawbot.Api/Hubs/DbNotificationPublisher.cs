using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

/// <summary>Persists the notification then pushes it realtime (per-user group, or tenant group for broadcast).</summary>
public sealed class DbNotificationPublisher(AppDbContext db, IHubContext<NotificationHub> hub) : INotificationPublisher
{
    public async Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var notification = Notification.Create(
            request.TenantId, request.UserId, request.Type, request.Title,
            DateTimeOffset.UtcNow, request.Severity, request.Body, request.Link);

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var payload = new
        {
            notification.Id,
            notification.Type,
            notification.Severity,
            notification.Title,
            notification.Body,
            notification.Link,
            notification.CreatedAt,
        };
        var target = request.UserId is { } userId
            ? hub.Clients.Group(NotificationHub.UserGroup(userId))
            : hub.Clients.Group(NotificationHub.TenantGroup(request.TenantId));
        await target.SendAsync("notification", payload, ct).ConfigureAwait(false);
    }
}
