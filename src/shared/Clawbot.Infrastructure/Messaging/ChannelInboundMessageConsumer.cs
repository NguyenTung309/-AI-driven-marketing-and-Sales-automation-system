using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

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
    private readonly ILogger<ChannelIn    boundMessageConsumer> _logger = logger;

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

    // Best-effort: Phản hồi thất bại không ảnh hưởng đến quá trình nhận tin nhắn 
    // loại bỏ các tin nhắn trùng lặp
    private async Task TryAutoReplyAsync(ChannelInboundMessageReceived msg, Guid conversationId, CancellationToken ct)
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
            // Tracked : có thể cần TryResumeAiAutoReply -> SaveChanges khi qua mốc hẹn.
            var conv = await _db.Conversations
                .IgnoreQueryFilters()
                .Where(c => c.Id == conversationId && c.TenantId == msg.TenantId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (conv is null || conv.Status != "open")
                return;

            // Sale gửi tay -> AI tạm tắt kèm mốc hẹn. Khách nhắn tiếp sau mốc đó thì AI tự bật lại và trả lời.
            if (!conv.AiAutoReplyEnabled)
            {
                if (!conv.TryResumeAiAutoReply(DateTimeOffset.UtcNow))
                    return;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                LogAutoReplyResumed(_logger, conversationId);
            }

            // Đã có 1 AI draft chờ duyệt (pending_approval, chưa gửi) trong hội thoại này thì KHÔNG sinh
            // thêm — người duyệt xử lý draft đó với ngữ cảnh mới nhất. Không có guard này, mỗi tin khách
            // mới lại đẻ 1 reply pending, xếp chồng nhiều draft cho cùng 1 khách. Approve/reject đổi status
            // khỏi pending_approval nên guard tự mở lại.
            // ponytail: ConcurrentMessageLimit=1 khử chồng tuần tự 1 host; 2 host cùng nhận 2 tin 1 lúc vẫn
            // có thể lọt 2 draft — thêm distributed lock/unique-index nếu race đó thành vấn đề thực.
            var hasPendingDraft = await _db.Messages
                .IgnoreQueryFilters()
                .AnyAsync(m => m.ConversationId == conversationId
                    && m.TenantId == msg.TenantId
                    && m.Direction == "out"
                    && m.Status == "pending_approval", ct)
                .ConfigureAwait(false);
            if (hasPendingDraft)
            {
                LogPendingDraftSkip(_logger, conversationId);
                return;
            }

            // Recent context, oldest-first, excluding the message just ingested.
            // ChatRequest.history is role-less; ChatAgent maps even/odd -> user/assistant, close enough for context.
            // Loại tin blocked (draft bị từ chối/chặn): khách chưa từng thấy, đưa vào history làm model tưởng đã trả lời.
            var history = await _db.Messages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.TenantId == msg.TenantId
                    && !(m.Direction == "out" && m.Status == "blocked"))
                .OrderByDescending(m => m.SentAt)
                .Skip(1)
                .Take(HistoryLimit)
                .OrderBy(m => m.SentAt)
                .Select(m => m.Content)
                .ToListAsync(ct).ConfigureAwait(false);

            // Strip HTML trước khi đưa vào ChatAgent: tin Pancake bọc <div>/<br> — đẩy raw vào LLM
            // vừa bẩn prompt vừa từng khiến model echo nguyên tin khách (kèm thẻ div) làm reply.
            var userText = ChannelMessageIngestor.StripHtml(msg.Message.Text);
            await _chatAgent.ReplyAsync(msg.TenantId, conversationId, userText, history, ct).ConfigureAwait(false);

            await _notifier.NotifyConversationUpdatedAsync(msg.TenantId,
                new InboxConversationEvent(conversationId, conv.Status, conv.AssignedTo, conv.LastMessageAt, conv.InboxId), ct).ConfigureAwait(false);
            LogAutoReplyProcessed(_logger, conversationId);
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
        Message = "AI auto-reply processing completed for conversation {ConversationId}")]
    private static partial void LogAutoReplyProcessed(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9113, Level = LogLevel.Information,
        Message = "AI auto-reply skipped for conversation {ConversationId}: a pending_approval draft already awaits review")]
    private static partial void LogPendingDraftSkip(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9114, Level = LogLevel.Information,
        Message = "AI auto-reply resumed for conversation {ConversationId}: customer replied after the sale-handover pause window")]
    private static partial void LogAutoReplyResumed(ILogger logger, Guid conversationId);

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
