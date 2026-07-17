using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Trả lời tin khách đang treo khi AI (vừa) được bật lại: dùng chung cho toggle tay (SetAiAutoReply true),
// sweep AiAutoReplyResumeJob (hết cửa sổ nhường sale), và nút "Tạo lại phản hồi AI" trên tin bị chặn.
// Khác đường consumer ở chỗ KHÔNG có tin inbound mới kích — nó tự lấy tin cuối của hội thoại và chỉ
// trả lời nếu tin đó là tin khách chưa được đáp.
public interface IAiAutoReplyResumer
{
    // Trả về true nếu đã kích pipeline trả lời; false nếu không có tin khách treo / guard chặn / lỗi.
    Task<bool> ReplyToHangingCustomerMessageAsync(Guid tenantId, Guid conversationId, CancellationToken ct);
}

public sealed partial class AiAutoReplyResumer(
    AppDbContext db,
    IChatAutoReplyGateway chatAgent,
    ILogger<AiAutoReplyResumer> logger) : IAiAutoReplyResumer
{
    private const int HistoryLimit = 10;

    private readonly AppDbContext _db = db;
    private readonly IChatAutoReplyGateway _chatAgent = chatAgent;
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
            // AI phải đang bật + hội thoại đang mở. Caller (toggle/sweep) chịu trách nhiệm bật trước khi gọi.
            if (conv is null || !conv.AiAutoReplyEnabled || conv.Status != "open")
                return false;

            // Tin cuối phải là tin khách chưa được đáp. Nếu tin cuối là "out" (sale/AI đã trả lời) thì
            // không có gì treo — return, tránh AI tự nhả thêm 1 tin thừa.
            // Tin "blocked" (draft bị người từ chối / bị chặn) chưa bao giờ tới khách — coi như không
            // tồn tại, để từ chối draft xong vẫn tạo lại được reply mới cho tin khách đang chờ.
            var lastMessage = await _db.Messages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.TenantId == tenantId
                    && !(m.Direction == "out" && m.Status == "blocked"))
                .OrderByDescending(m => m.SentAt)
                .Select(m => new { m.Direction, m.Content, m.MessageType })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (lastMessage is null || lastMessage.Direction != "in")
                return false;
            // Comment không đi đường chat reply_inbox (giống consumer) — CommentAutoReplyJob lo.
            if (string.Equals(lastMessage.MessageType, "comment", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.IsNullOrWhiteSpace(lastMessage.Content))
                return false;

            // Đã có draft chờ duyệt thì không sinh thêm (giống ChannelInboundMessageConsumer).
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

            // History oldest-first, bỏ tin cuối (chính là tin đang trả lời). Loại tin blocked:
            // khách chưa từng thấy, đưa vào history làm model tưởng đã trả lời rồi.
            var history = await _db.Messages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId && m.TenantId == tenantId
                    && !(m.Direction == "out" && m.Status == "blocked"))
                .OrderByDescending(m => m.SentAt)
                .Skip(1)
                .Take(HistoryLimit)
                .OrderBy(m => m.SentAt)
                .Select(m => m.Content)
                .ToListAsync(ct).ConfigureAwait(false);

            var userText = ChannelMessageIngestor.StripHtml(lastMessage.Content);
            await _chatAgent.ReplyAsync(tenantId, conversationId, userText, history, ct).ConfigureAwait(false);
            LogHangingReplied(_logger, conversationId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogHangingFailed(_logger, ex, conversationId);
            return false;
        }
    }

    [LoggerMessage(EventId = 9120, Level = LogLevel.Information,
        Message = "AI replied to hanging customer message for conversation {ConversationId} after auto-reply resumed")]
    private static partial void LogHangingReplied(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9121, Level = LogLevel.Information,
        Message = "Hanging-message reply skipped for conversation {ConversationId}: a pending_approval draft already awaits review")]
    private static partial void LogHangingDraftSkip(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9122, Level = LogLevel.Warning,
        Message = "Hanging-message reply failed for conversation {ConversationId}")]
    private static partial void LogHangingFailed(ILogger logger, Exception ex, Guid conversationId);
}
