using System.Text.Json;
using Clawbot.Infrastructure.Notifications;
using Clawbot.SharedKernel.Inbox;
using StackExchange.Redis;

namespace Clawbot.Api.Hubs;

/// <summary>
/// Relays inbox events published on Redis by hub-less processes (AgentService — e.g. the
/// channel-inbound consumer landing there as a competing consumer) into InboxHub via the local
/// IInboxNotifier, so message/typing/conversation updates reach the FE realtime regardless of
/// which host processed the message.
/// </summary>
public sealed partial class RedisInboxEventRelay(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILogger<RedisInboxEventRelay> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(RedisBridgeInboxNotifier.Channel),
            (channel, value) => { _ = RelayAsync(value); }).ConfigureAwait(false);

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
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisBridgeInboxNotifier.Channel)).ConfigureAwait(false);
        }
    }

    private async Task RelayAsync(RedisValue value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value.ToString());
            var root = doc.RootElement;
            var kind = root.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;
            if (string.IsNullOrEmpty(kind)
                || !root.TryGetProperty("tenantId", out var tenantProp) || !tenantProp.TryGetGuid(out var tenantId)
                || !root.TryGetProperty("payload", out var payloadProp))
                return;

            // SignalRInboxNotifier is scoped — resolve per event.
            using var scope = scopeFactory.CreateScope();
            var notifier = scope.ServiceProvider.GetRequiredService<IInboxNotifier>();
            var payloadJson = payloadProp.GetRawText();
            switch (kind)
            {
                case "message":
                    if (Deserialize<InboxMessageEvent>(payloadJson) is { } messageEvt)
                        await notifier.NotifyMessageAsync(tenantId, messageEvt).ConfigureAwait(false);
                    break;
                case "messageStatus":
                    if (Deserialize<InboxMessageStatusEvent>(payloadJson) is { } statusEvt)
                        await notifier.NotifyMessageStatusAsync(tenantId, statusEvt).ConfigureAwait(false);
                    break;
                case "conversation":
                    if (Deserialize<InboxConversationEvent>(payloadJson) is { } conversationEvt)
                        await notifier.NotifyConversationUpdatedAsync(tenantId, conversationEvt).ConfigureAwait(false);
                    break;
                case "typing":
                    if (Deserialize<InboxTypingEvent>(payloadJson) is { } typingEvt)
                        await notifier.NotifyTypingAsync(tenantId, typingEvt).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
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

    private static TEvent? Deserialize<TEvent>(string json) where TEvent : class =>
        JsonSerializer.Deserialize<TEvent>(json, JsonOpts);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Warning, Message = "Failed to relay a Redis inbox event to SignalR")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception);
}
