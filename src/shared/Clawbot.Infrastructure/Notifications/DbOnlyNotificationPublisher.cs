using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Notifications;

/// <summary>
/// Persist-only notification publisher for processes that have no SignalR hub (e.g. AgentService).
/// Writes the notification row; realtime push is handled API-side, and the FE also polls the
/// unread count, so the alert still surfaces. Singleton-safe via a per-call scope.
/// </summary>
public sealed class DbOnlyNotificationPublisher(IServiceScopeFactory scopeFactory) : INotificationPublisher
{
    public async Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Notifications.Add(Notification.Create(
            request.TenantId, request.UserId, request.Type, request.Title,
            DateTimeOffset.UtcNow, request.Severity, request.Body, request.Link));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
