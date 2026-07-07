using System.Text.Json;
using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Clawbot.Infrastructure.Notifications;

/// <summary>Redis channel for orchestration run-progress events (AgentService → API relay → SignalR).</summary>
public static class RunEventsChannel
{
    public const string Name = "clawbot:run-events";
}

/// <summary>Wire shape published on the Redis notification channel (consumed by the API-side relay).</summary>
public sealed record NotificationBridgePayload(
    Guid TenantId,
    Guid? UserId,
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string? Body,
    string? Link,
    DateTimeOffset CreatedAt);

/// <summary>
/// Notification publisher for processes without a SignalR hub (AgentService): persists the row,
/// then publishes it to Redis pub/sub so the API-side relay can push it realtime through
/// NotificationHub. If Redis publish fails the row is already saved — the FE unread polling
/// still surfaces it, so the failure is swallowed by design.
/// </summary>
public sealed class RedisBridgeNotificationPublisher(
    IServiceScopeFactory scopeFactory,
    IConnectionMultiplexer redis) : INotificationPublisher
{
    public const string Channel = "clawbot:notifications";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Notification notification;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            notification = Notification.Create(
                request.TenantId, request.UserId, request.Type, request.Title,
                DateTimeOffset.UtcNow, request.Severity, request.Body, request.Link);
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var payload = new NotificationBridgePayload(
            request.TenantId, request.UserId, notification.Id, notification.Type,
            notification.Severity, notification.Title, notification.Body, notification.Link, notification.CreatedAt);
        try
        {
            await redis.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(Channel), JsonSerializer.Serialize(payload, JsonOpts))
                .ConfigureAwait(false);
        }
        catch (RedisException)
        {
            // ponytail: realtime push lost — DB row persisted, FE unread polling still surfaces it.
        }
    }
}
