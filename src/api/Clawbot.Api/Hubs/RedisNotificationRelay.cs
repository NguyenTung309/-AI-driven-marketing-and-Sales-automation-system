using System.Text.Json;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Notifications;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Clawbot.Api.Hubs;

/// <summary>
/// Relays notifications published on Redis by hub-less processes (AgentService — e.g. run
/// failed / plan pending approval) into NotificationHub, so the bell + toast update realtime
/// without the FE having to poll. The row is already persisted by the publisher; this only pushes.
/// </summary>
public sealed partial class RedisNotificationRelay(
    IConnectionMultiplexer redis,
    IHubContext<NotificationHub> hub,
    ILogger<RedisNotificationRelay> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisBridgeNotificationPublisher.Channel),
            (channel, value) => { _ = RelayAsync(value); }).ConfigureAwait(false);
        // C1: run-progress nudges — FE invalidates the run queries instead of polling every 3s.
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RunEventsChannel.Name),
            (channel, value) => { _ = RelayRunEventAsync(value); }).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisBridgeNotificationPublisher.Channel)).ConfigureAwait(false);
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(RunEventsChannel.Name)).ConfigureAwait(false);
        }
    }

    private async Task RelayRunEventAsync(RedisValue value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value.ToString());
            var tenantId = doc.RootElement.TryGetProperty("tenantId", out var tenantProp) ? tenantProp.GetString() : null;
            var sessionId = doc.RootElement.TryGetProperty("sessionId", out var sessionProp) ? sessionProp.GetString() : null;
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(sessionId))
                return;

            await hub.Clients.Group(NotificationHub.TenantGroup(tenantId))
                .SendAsync("runUpdated", new { sessionId }).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            LogRelayFailed(logger, ex);
        }
        catch (RedisException ex)
        {
            LogRelayFailed(logger, ex);
        }
    }

    private async Task RelayAsync(RedisValue value)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<NotificationBridgePayload>(value.ToString(), JsonOpts);
            if (payload is null)
                return;

            var message = new
            {
                payload.Id,
                payload.Type,
                payload.Severity,
                payload.Title,
                payload.Body,
                payload.Link,
                payload.CreatedAt,
                payload.OccurrenceCount,
                Push = NotificationDeliveryPolicy.IsAlwaysPushed(payload.Severity)
                    || NotificationDeliveryPolicy.DefaultPush(payload.Type),
            };
            var target = payload.UserId is { } userId
                ? hub.Clients.Group(NotificationHub.UserGroup(userId))
                : hub.Clients.Group(NotificationHub.TenantGroup(payload.TenantId));
            await target.SendAsync("notification", message).ConfigureAwait(false);

            // Thông báo do AgentService sinh (chạy khác host, không có Hangfire client) —
            // web push enqueue tại đây, nơi có Hangfire.
            BackgroundJob.Enqueue<WebPushDispatchJob>(j => j.SendAsync(payload.Id, CancellationToken.None));
        }
        catch (JsonException ex)
        {
            LogRelayFailed(logger, ex);
        }
        catch (RedisException ex)
        {
            LogRelayFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 7301, Level = LogLevel.Warning, Message = "Failed to relay a Redis notification to SignalR")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception);
}
