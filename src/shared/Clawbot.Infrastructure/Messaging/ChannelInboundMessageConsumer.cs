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
// After ingesting an inbound customer message, KHÔNG trả lời ngay: chỉ đặt/reset đồng hồ debounce
// (AiReplyDebouncer) — khách nhắn dồn nhiều tin thì hết cửa sổ AI mới trả lời MỘT lần cho cả khối
// qua IAiAutoReplyResumer (nó tự đọc khối tin treo + guard pending-draft + typing + gửi reply).
public sealed partial class ChannelInboundMessageConsumer(
    IChannelMessageIngestor ingestor,
    AppDbContext db,
    IAiReplyDebouncer debouncer,
    ILogger<ChannelInboundMessageConsumer> logger)
    : IConsumer<ChannelInboundMessageReceived>
{
    private readonly IChannelMessageIngestor _ingestor = ingestor;
    private readonly AppDbContext _db = db;
    private readonly IAiReplyDebouncer _debouncer = debouncer;
    private readonly ILogger<ChannelInboundMessageConsumer> _logger = logger;

    public async Task Consume(ConsumeContext<ChannelInboundMessageReceived> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var result = await _ingestor.IngestAsync(msg.TenantId, msg.Message, context.CancellationToken).ConfigureAwait(false);
        LogIngested(_logger, msg.Message.ExternalThreadId, result.ConversationId, result.Deduplicated);

        if (!result.Deduplicated)
        {
            await TryScheduleAutoReplyAsync(msg, result.ConversationId, context.CancellationToken).ConfigureAwait(false);
        }
    }

    // Best-effort: a scheduling failure must not fail the ingest (throwing would redeliver the message,
    // the ingest would dedup, and the reply would never be retried anyway).
    private async Task TryScheduleAutoReplyAsync(ChannelInboundMessageReceived msg, Guid conversationId, CancellationToken ct)
    {
        // Comment thread: chat auto-reply gửi reply_inbox là SAI ngữ nghĩa (phải reply_comment/private_replies)
        // — CommentAutoReplyJob (scan định kỳ) lo loại này.
        if (string.Equals(msg.Message.MessageType, "comment", StringComparison.OrdinalIgnoreCase))
            return;
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
            // Tracked (không AsNoTracking): có thể cần TryResumeAiAutoReply -> SaveChanges khi qua mốc hẹn.
            var conv = await _db.Conversations
                .IgnoreQueryFilters()
                .Where(c => c.Id == conversationId && c.TenantId == msg.TenantId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (conv is null || conv.Status != "open")
                return;

            // Sale gửi tay -> AI tạm tắt kèm mốc hẹn. Khách nhắn tiếp sau mốc đó thì AI tự bật lại và trả lời.
            // Resume nằm ở thời điểm ingest (không phải lúc debounce fire) vì ngữ nghĩa gắn với "khách nhắn tiếp".
            if (!conv.AiAutoReplyEnabled)
            {
                if (!conv.TryResumeAiAutoReply(DateTimeOffset.UtcNow))
                    return;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                LogAutoReplyResumed(_logger, conversationId);
            }

            // Các guard còn lại (pending-draft, khối tin treo, typing, notify) chạy lúc debounce fire
            // trong AiAutoReplyResumer — lúc đó mới biết trạng thái cuối cùng của hội thoại.
            _debouncer.Schedule(msg.TenantId, conversationId);
            LogAutoReplyScheduled(_logger, conversationId);
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
        Message = "AI auto-reply debounce scheduled for conversation {ConversationId}")]
    private static partial void LogAutoReplyScheduled(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9114, Level = LogLevel.Information,
        Message = "AI auto-reply resumed for conversation {ConversationId}: customer replied after the sale-handover pause window")]
    private static partial void LogAutoReplyResumed(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9112, Level = LogLevel.Warning,
        Message = "AI auto-reply scheduling failed for conversation {ConversationId}")]
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
