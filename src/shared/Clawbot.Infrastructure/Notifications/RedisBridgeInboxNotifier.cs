using System.Text.Json;
using Clawbot.SharedKernel.Inbox;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Clawbot.Infrastructure.Notifications;

/// <summary>
/// Inbox notifier for processes without the SignalR hub (AgentService): publishes inbox events
/// to Redis pub/sub so the API-side RedisInboxEventRelay pushes them through InboxHub. Realtime
/// loss must never fail ingest/auto-reply, so Redis failures are logged and swallowed — the FE
/// still catches up on the next query refetch.
/// </summary>
public sealed partial class RedisBridgeInboxNotifier(
    IConnectionMultiplexer redis,
    ILogger<RedisBridgeInboxNotifier> logger) : IInboxNotifier
{
    public const string Channel = "clawbot:inbox-events";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default) =>
        PublishAsync("message", tenantId, evt);

    public Task NotifyMessageStatusAsync(Guid tenantId, InboxMessageStatusEvent evt, CancellationToken ct = default) =>
        PublishAsync("messageStatus", tenantId, evt);

    public Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default) =>
        PublishAsync("conversation", tenantId, evt);

    public Task NotifyTypingAsync(Guid tenantId, InboxTypingEvent evt, CancellationToken ct = default) =>
        PublishAsync("typing", tenantId, evt);

    private async Task PublishAsync<TEvent>(string kind, Guid tenantId, TEvent evt)
    {
        var envelope = new InboxEventBridgePayload<TEvent>(kind, tenantId, evt);
        try
        {
            await redis.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(Channel), JsonSerializer.Serialize(envelope, JsonOpts))
                .ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            LogPublishFailed(logger, ex, kind);
        }
    }

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning,
        Message = "Failed to publish inbox {Kind} event to the Redis bridge; realtime push lost")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string kind);
}

/// <summary>Wire shape on the Redis inbox-events channel (consumed by the API-side relay).</summary>
public sealed record InboxEventBridgePayload<TEvent>(string Kind, Guid TenantId, TEvent Payload);
