using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Clawbot.SharedKernel.Notifications;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class CommentAutoReplyJob(
    AppDbContext db,
    IIntentClassifier intent,
    IClock clock,
    INotificationPublisher publisher,
    ILogger<CommentAutoReplyJob> logger,
    ICommentChannelAdapter? commentAdapter = null)
{
    private static readonly HashSet<string> ActionableLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "purchase_intent",
        "ask_price",
        "book_trial",
    };

    // Đường polling: comment ingest qua bus consumer (chạy đa host, không enqueue Hangfire được) —
    // scan định kỳ quét comment mới rồi tái dùng RunAsync (đã idempotent qua alreadyReplied).
    public async Task RunScanAsync(CancellationToken ct = default)
    {
        var since = clock.UtcNow.AddMinutes(-30);
        var candidates = await db.Messages.IgnoreQueryFilters()
            .Where(m => m.Direction == "in" && m.MessageType == "comment" && m.SentAt >= since)
            .OrderBy(m => m.SentAt)
            .Take(100)
            .Select(m => new { m.Id, m.TenantId })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            try
            {
                await RunAsync(candidate.TenantId, candidate.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCandidateFailed(logger, ex, candidate.Id);
            }
        }
    }

    public async Task RunAsync(Guid tenantId, Guid messageId, CancellationToken ct)
    {
        // Pancake là kênh duy nhất hỗ trợ reply_comment/private_replies — thiếu adapter thì skip hẳn,
        // KHÔNG fallback reply_inbox (bắn nhầm action vào comment thread).
        if (commentAdapter is null)
        {
            LogNoCommentAdapter(logger);
            return;
        }
        var inbound = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (inbound is null || !string.Equals(inbound.MessageType, "comment", StringComparison.OrdinalIgnoreCase))
            return;

        var alreadyReplied = await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == inbound.ConversationId
                && m.Direction == "out"
                && m.SenderType == "bot"
                && m.MessageType == "comment"
                && m.ParentPostId == inbound.ParentPostId, ct)
            .ConfigureAwait(false);
        if (alreadyReplied) return;

        var text = inbound.OriginalContent ?? inbound.Content;
        var detected = await intent.ClassifyAsync(text, "vi-VN", ct).ConfigureAwait(false);
        if (detected.Confidence < 0.5f || !ActionableLabels.Contains(detected.Label))
            return;

        var conversation = await db.Conversations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == inbound.ConversationId && c.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (conversation is null) return;

        // Review-gate P3: tôn trọng manual-mode/handover — sale đang cầm hội thoại (AiAutoReplyEnabled=false,
        // do Escalate() hoặc sale gửi tay) hoặc hội thoại đã đóng thì bot KHÔNG tự bắn comment reply + DM.
        if (!conversation.AiAutoReplyEnabled || string.Equals(conversation.Status, "resolved", StringComparison.OrdinalIgnoreCase))
        {
            LogCommentAutoReplySkipped(logger, conversation.Id, conversation.AiAutoReplyEnabled, conversation.Status);
            return;
        }

        // reply_comment cần id của chính comment; ingest luôn lưu external_message_id từ Pancake.
        if (string.IsNullOrEmpty(inbound.ExternalMessageId))
        {
            LogMissingIds(logger, conversation.Id, "external_message_id");
            return;
        }

        // Review-gate P5 (QĐ6 template-approved): 2 câu dưới là text TĨNH 100%, không interpolate dữ liệu
        // ngoài — được duyệt 1 lần tại đây (code review = review). Nếu sau này thêm biến động (tên khách,
        // nội dung LLM sinh), bản render PHẢI qua toxicity trước khi gửi (xem DripSequenceJob).
        var reply = "Cảm ơn bạn đã quan tâm. Mình đã nhắn riêng để tư vấn chi tiết ngay.";
        var dm = "Chào bạn, mình gửi thông tin chi tiết tại đây. Bạn cho mình biết mục tiêu học và số điện thoại để tư vấn nhanh nhé.";

        // 1) Rep công khai dưới comment (action reply_comment + message_id).
        var replyExternalId = await commentAdapter.SendCommentReplyAsync(
            conversation.TenantId, conversation.ExternalThreadId, inbound.ExternalMessageId, reply, ct).ConfigureAwait(false);
        conversation.AppendMessage("out", "bot", reply, "text", clock.UtcNow,
            externalMessageId: replyExternalId, messageType: "comment", parentPostId: inbound.ParentPostId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 2) DM riêng (action private_replies) — cần post_id + from_id (commenter). FB cho 1 lần/comment.
        var fromId = conversation.ContactId.HasValue
            ? await db.ContactExternalIds.IgnoreQueryFilters()
                .Where(x => x.ContactId == conversation.ContactId.Value && x.Platform == conversation.Platform)
                .Select(x => x.ExternalId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        if (string.IsNullOrEmpty(inbound.ParentPostId) || string.IsNullOrEmpty(fromId))
        {
            LogMissingIds(logger, conversation.Id, string.IsNullOrEmpty(inbound.ParentPostId) ? "post_id" : "from_id");
        }
        else
        {
            try
            {
                var dmExternalId = await commentAdapter.SendPrivateReplyAsync(
                    conversation.TenantId, conversation.ExternalThreadId, inbound.ParentPostId, inbound.ExternalMessageId, fromId, dm, ct).ConfigureAwait(false);
                conversation.AppendMessage("out", "bot", dm, "text", clock.UtcNow,
                    externalMessageId: dmExternalId, messageType: "dm", parentPostId: inbound.ParentPostId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPrivateReplyFailed(logger, ex, conversation.Id, inbound.ExternalMessageId);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogCommentAutoReplySent(logger, conversation.Id, inbound.ParentPostId ?? string.Empty, detected.Label);
        // AI trả lời comment khách: gom nhóm theo ngày làm việc — sale mở feed thấy "x8" là biết mức độ.
        await publisher.PublishAsync(new NotificationRequest(
            conversation.TenantId,
            UserId: null,
            Type: "comment_auto_reply",
            Title: "AI đã tự trả lời bình luận khách",
            Severity: "info",
            Body: $"Ý định: {detected.Label}. Mở hộp thư để xem hội thoại.",
            Link: "/inbox",
            GroupKey: "comment.autoreply"), ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 9201, Level = LogLevel.Information,
        Message = "Chat-2 comment auto-reply sent for conversation {ConversationId}, post {ParentPostId}, intent {IntentLabel}")]
    private static partial void LogCommentAutoReplySent(
        ILogger logger,
        Guid conversationId,
        string parentPostId,
        string intentLabel);

    [LoggerMessage(EventId = 9202, Level = LogLevel.Information,
        Message = "Chat-2 comment auto-reply skipped for conversation {ConversationId}: aiAutoReplyEnabled={AiAutoReplyEnabled}, status={Status}")]
    private static partial void LogCommentAutoReplySkipped(
        ILogger logger,
        Guid conversationId,
        bool aiAutoReplyEnabled,
        string status);

    [LoggerMessage(EventId = 9203, Level = LogLevel.Warning,
        Message = "Chat-2 comment auto-reply skipped: no ICommentChannelAdapter registered on this host")]
    private static partial void LogNoCommentAdapter(ILogger logger);

    [LoggerMessage(EventId = 9204, Level = LogLevel.Warning,
        Message = "Chat-2 comment auto-reply for conversation {ConversationId} missing {MissingField} — public reply/DM skipped accordingly")]
    private static partial void LogMissingIds(ILogger logger, Guid conversationId, string missingField);

    [LoggerMessage(EventId = 9205, Level = LogLevel.Warning,
        Message = "Chat-2 private reply failed for conversation {ConversationId}, comment {CommentId}")]
    private static partial void LogPrivateReplyFailed(ILogger logger, Exception ex, Guid conversationId, string commentId);

    [LoggerMessage(EventId = 9206, Level = LogLevel.Warning,
        Message = "Chat-2 comment auto-reply candidate failed for conversation {ConversationId}")]
    private static partial void LogCandidateFailed(ILogger logger, Exception ex, Guid conversationId);
}
