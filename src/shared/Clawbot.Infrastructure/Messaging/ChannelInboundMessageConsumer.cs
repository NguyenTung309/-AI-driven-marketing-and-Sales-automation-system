using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Ingests channel messages published by PancakePollingService (and any future adapter path).
// At-least-once delivery is safe: the ingestor dedups on external_message_id.
// After ingesting an inbound customer message, triggers the AI auto-reply when the conversation
// has the "AI dang chat" flag on — the ChatAgent gRPC call persists the reply and sends it to
// the channel itself (SPEC-16 P2-10), so this consumer only drains the stream.
public sealed partial class ChannelInboundMessageConsumer(
    IChannelMessageIngestor ingestor,
    AppDbContext db,
    IInboxNotifier notifier,
    IChatAutoReplyGateway chatAgent,
    ILogger<ChannelInboundMessageConsumer> logger)
    : IConsumer<ChannelInboundMessageReceived>
{
    private const int HistoryLimit = 10;

    private readonly IChannelMessageIngestor _ingestor = ingestor;
    private readonly AppDbContext _db = db;
    private readonly IInboxNotifier _notifier = notifier;
    private readonly IChatAutoReplyGateway _chatAgent = chatAgent;
    private readonly ILogger<ChannelInboundMessageConsumer> _logger = logger;

    public async Task Consume(ConsumeContext<ChannelInboundMessageReceived> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var result = await _ingestor.IngestAsync(msg.TenantId, msg.Message, context.CancellationToken).ConfigureAwait(false);
        LogIngested(_logger, msg.Message.ExternalThreadId, result.ConversationId, result.Deduplicated);

        if (!result.Deduplicated)
        {
            await TryAutoReplyAsync(msg, result.ConversationId, context.CancellationToken).ConfigureAwait(false);
        }
    }

    // Best-effort: a reply failure must not fail the ingest (throwing would redeliver the message,
    // the ingest would dedup, and the reply would never be retried anyway).
    private async Task TryAutoReplyAsync(ChannelInboundMessageReceived msg, Guid conversationId, CancellationToken ct)
    {
        // Only reply to customer messages, never to owner/AI echo
        if (msg.Message.Metadata.TryGetValue("is_owner", out var owner) && string.Equals(owner, "true", StringComparison.OrdinalIgnoreCase))
            return;
        if (msg.Message.Metadata.TryGetValue("sender_id", out var senderId)
            && msg.Message.Metadata.TryGetValue("page_id", out var pageId)
            && !string.IsNullOrEmpty(senderId) && string.Equals(senderId, pageId, StringComparison.Ordinal))
            return;
        if (string.IsNullOrWhiteSpace(msg.Message.Text))
            return;

        try
        {
            var conv = await _db.Conversations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.Id == conversationId && c.TenantId == msg.TenantId)
                .Select(c => new { c.AiAutoReplyEnabled, c.Status, c.AssignedTo, c.LastMessageAt })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (conv is null || !conv.AiAutoReplyEnabled || conv.Status != "open")
                return;

            // Recent context, oldest-first, excluding the message just ingested.
            // ChatRequest.history is role-less; ChatAgent maps even/odd -> user/assistant, close enough for context.
            var history = await _db.Messages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.TenantId == msg.TenantId)
                .OrderByDescending(m => m.SentAt)
                .Skip(1)
                .Take(HistoryLimit)
                .OrderBy(m => m.SentAt)
                .Select(m => m.Content)
                .ToListAsync(ct).ConfigureAwait(false);

            await _chatAgent.ReplyAsync(msg.TenantId, conversationId, msg.Message.Text, history, ct).ConfigureAwait(false);

            await _notifier.NotifyConversationUpdatedAsync(msg.TenantId,
                new InboxConversationEvent(conversationId, conv.Status, conv.AssignedTo, conv.LastMessageAt), ct).ConfigureAwait(false);
            LogAutoReplied(_logger, conversationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAutoReplyFailed(_logger, ex, conversationId);
        }
    }

    [LoggerMessage(EventId = 9110, Level = LogLevel.Information,
        Message = "Ingested inbound channel message for thread {ThreadId} into conversation {ConversationId} (deduplicated: {Deduplicated})")]
    private static partial void LogIngested(ILogger logger, string threadId, Guid conversationId, bool deduplicated);

    [LoggerMessage(EventId = 9111, Level = LogLevel.Information,
        Message = "AI auto-reply sent for conversation {ConversationId}")]
    private static partial void LogAutoReplied(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9112, Level = LogLevel.Warning,
        Message = "AI auto-reply failed for conversation {ConversationId}")]
    private static partial void LogAutoReplyFailed(ILogger logger, Exception ex, Guid conversationId);
}

// ponytail: ConcurrentMessageLimit=1 keeps per-conversation ordering with today's traffic;
// partition by conversation id if throughput ever matters.
public sealed class ChannelInboundMessageConsumerDefinition : ConsumerDefinition<ChannelInboundMessageConsumer>
{
    public ChannelInboundMessageConsumerDefinition()
    {
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ChannelInboundMessageConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(endpointConfigurator);
        // Transient DB/embedding hiccups: brief in-place retries before the message goes to the error queue.
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
