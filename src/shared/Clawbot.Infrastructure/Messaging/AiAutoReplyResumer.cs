using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Trả lời tin khách đang treo khi AI (vừa) được bật lại: dùng chung cho toggle tay (SetAiAutoReply true),
// sweep AiAutoReplyResumeJob (hết cửa sổ nhường sale), nút "Tạo lại phản hồi AI" trên tin bị chặn, và
// AiReplyDebouncer (gom tin nhắn liên tiếp của khách rồi trả lời một lần).
// Khác đường consumer ở chỗ KHÔNG có tin inbound mới kích — nó tự lấy KHỐI tin khách cuối hội thoại
// (mọi tin "in" liên tiếp chưa được đáp, gộp thành 1 prompt) và chỉ trả lời nếu khối đó tồn tại.
public interface IAiAutoReplyResumer
{
    // Trả về true nếu đã kích pipeline trả lời; false nếu không có tin khách treo / guard chặn / lỗi.
    Task<bool> ReplyToHangingCustomerMessageAsync(Guid tenantId, Guid conversationId, CancellationToken ct);
}

public sealed partial class AiAutoReplyResumer(
    AppDbContext db,
    IChatAutoReplyGateway chatAgent,
    IInboxNotifier notifier,
    ILogger<AiAutoReplyResumer> logger) : IAiAutoReplyResumer
{
    private const int HistoryLimit = 10;
    // Đủ chứa khối tin khách liên tiếp dài bất thường + 10 tin history phía trước.
    private const int RecentMessageLimit = 30;

    private readonly AppDbContext _db = db;
    private readonly IChatAutoReplyGateway _chatAgent = chatAgent;
    private readonly IInboxNotifier _notifier = notifier;
    private readonly ILogger<AiAutoReplyResumer> _logger = logger;

    public async Task<bool> ReplyToHangingCustomerMessageAsync(Guid tenantId, Guid conversationId, CancellationToken ct)
    {
        try
        {
            var conv = await _db.Conversations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.Id == conversationId && c.TenantId == tenantId)
                .Select(c => new { c.AiAutoReplyEnabled, c.Status, c.AssignedTo, c.LastMessageAt, c.InboxId })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            // AI phải đang bật + hội thoại đang mở. Caller (toggle/sweep/debouncer) chịu trách nhiệm bật trước khi gọi.
            if (conv is null || !conv.AiAutoReplyEnabled || conv.Status != "open")
                return false;

            // Tin "blocked" (draft bị người từ chối / bị chặn) chưa bao giờ tới khách — coi như không
            // tồn tại, để từ chối draft xong vẫn tạo lại được reply mới cho tin khách đang chờ.
            var recent = (await _db.Messages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.TenantId == tenantId
                    && !(m.Direction == "out" && m.Status == "blocked"))
                .OrderByDescending(m => m.SentAt)
                .Take(RecentMessageLimit)
                .Select(m => new { m.Direction, m.MessageType, m.Content })
                .ToListAsync(ct).ConfigureAwait(false))
                .Select(m => new ReplyContextMessage(m.Direction, m.MessageType, m.Content))
                .ToList();

            // Khối đuôi = mọi tin "in" liên tiếp chưa được đáp, gộp thành 1 userText (khách nhắn dồn
            // nhiều tin thì AI trả lời đủ ý một lần). Null khi tin cuối là "out" (không có gì treo)
            // hoặc khối chỉ toàn comment/tin rỗng (comment đi đường CommentAutoReplyJob).
            var block = BuildTrailingCustomerBlock(recent, HistoryLimit);
            if (block is null)
                return false;

            // Đã có draft chờ duyệt thì không sinh thêm (giống ChannelInboundMessageConsumer trước đây).
            var hasPendingDraft = await _db.Messages
                .IgnoreQueryFilters()
                .AnyAsync(m => m.ConversationId == conversationId
                    && m.TenantId == tenantId
                    && m.Direction == "out"
                    && m.Status == "pending_approval", ct)
                .ConfigureAwait(false);
            if (hasPendingDraft)
            {
                LogHangingDraftSkip(_logger, conversationId);
                return false;
            }

            // Báo FE "AI đang soạn" trước khi generate; luôn clear ở finally (kể cả khi reply lỗi/cancel)
            // để bong bóng typing không kẹt trên UI.
            await NotifyTypingSafeAsync(tenantId, conversationId, conv.AssignedTo, conv.InboxId, isTyping: true, ct).ConfigureAwait(false);
            try
            {
                await _chatAgent.ReplyAsync(tenantId, conversationId, block.Value.UserText, block.Value.History, ct).ConfigureAwait(false);
            }
            finally
            {
                // CancellationToken.None: nếu ct đã cancel thì lệnh clear vẫn phải đi.
                await NotifyTypingSafeAsync(tenantId, conversationId, conv.AssignedTo, conv.InboxId, isTyping: false, CancellationToken.None).ConfigureAwait(false);
            }

            await _notifier.NotifyConversationUpdatedAsync(tenantId,
                new InboxConversationEvent(conversationId, conv.Status, conv.AssignedTo, conv.LastMessageAt, conv.InboxId), ct).ConfigureAwait(false);
            LogHangingReplied(_logger, conversationId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHangingFailed(_logger, ex, conversationId);
            return false;
        }
    }

    internal readonly record struct ReplyContextMessage(string Direction, string? MessageType, string? Content);

    // Thuần để unit test không cần DB. Input: tin mới nhất trước (đã loại out+blocked).
    // Output: userText = các tin "in" liên tiếp cuối hội thoại (oldest-first, strip HTML, nối bằng \n),
    // history = các tin ngay trước khối đó (oldest-first, tối đa historyLimit, giữ nguyên content thô
    // như hành vi cũ). Null nếu không có tin, tin cuối là "out", hoặc khối toàn comment/rỗng.
    internal static (string UserText, List<string> History)? BuildTrailingCustomerBlock(
        IReadOnlyList<ReplyContextMessage> newestFirst, int historyLimit)
    {
        if (newestFirst.Count == 0 || !string.Equals(newestFirst[0].Direction, "in", StringComparison.Ordinal))
            return null;

        var blockSize = 0;
        while (blockSize < newestFirst.Count && string.Equals(newestFirst[blockSize].Direction, "in", StringComparison.Ordinal))
            blockSize++;

        var texts = new List<string>(blockSize);
        for (var i = blockSize - 1; i >= 0; i--)
        {
            var msg = newestFirst[i];
            if (string.Equals(msg.MessageType, "comment", StringComparison.OrdinalIgnoreCase))
                continue;
            // Strip HTML trước khi đưa vào ChatAgent: tin Pancake bọc <div>/<br> — đẩy raw vào LLM
            // vừa bẩn prompt vừa từng khiến model echo nguyên tin khách (kèm thẻ div) làm reply.
            var clean = ChannelMessageIngestor.StripHtml(msg.Content ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(clean))
                texts.Add(clean);
        }

        if (texts.Count == 0)
            return null;

        var history = newestFirst
            .Skip(blockSize)
            .Take(historyLimit)
            .Reverse()
            .Select(m => m.Content ?? string.Empty)
            .ToList();

        return (string.Join("\n", texts), history);
    }

    // Best-effort: notify lỗi không được làm hỏng auto-reply.
    private async Task NotifyTypingSafeAsync(Guid tenantId, Guid conversationId, Guid? assignedTo, Guid? inboxId, bool isTyping, CancellationToken ct)
    {
        try
        {
            await _notifier.NotifyTypingAsync(tenantId,
                new InboxTypingEvent(conversationId, isTyping, "ai", assignedTo, inboxId), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTypingNotifyFailed(_logger, ex, conversationId, isTyping);
        }
    }

    [LoggerMessage(EventId = 9120, Level = LogLevel.Information,
        Message = "AI replied to hanging customer message block for conversation {ConversationId}")]
    private static partial void LogHangingReplied(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9121, Level = LogLevel.Information,
        Message = "Hanging-message reply skipped for conversation {ConversationId}: a pending_approval draft already awaits review")]
    private static partial void LogHangingDraftSkip(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9122, Level = LogLevel.Warning,
        Message = "Hanging-message reply failed for conversation {ConversationId}")]
    private static partial void LogHangingFailed(ILogger logger, Exception ex, Guid conversationId);

    [LoggerMessage(EventId = 9123, Level = LogLevel.Warning,
        Message = "Failed to notify typing={IsTyping} for conversation {ConversationId}; realtime indicator lost")]
    private static partial void LogTypingNotifyFailed(ILogger logger, Exception ex, Guid conversationId, bool isTyping);
}
