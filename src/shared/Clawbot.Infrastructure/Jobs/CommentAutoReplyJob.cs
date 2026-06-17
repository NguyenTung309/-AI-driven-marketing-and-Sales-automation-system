using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class CommentAutoReplyJob(
    AppDbContext db,
    IChannelAdapter adapter,
    IIntentClassifier intent,
    IClock clock,
    ILogger<CommentAutoReplyJob> logger)
{
    private static readonly HashSet<string> ActionableLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "purchase_intent",
        "ask_price",
        "book_trial",
    };

    public async Task RunAsync(Guid tenantId, Guid messageId, CancellationToken ct)
    {
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

        var reply = "Cảm ơn bạn đã quan tâm. Mình đã nhắn riêng để tư vấn chi tiết ngay.";
        var dm = "Chào bạn, mình gửi thông tin chi tiết tại đây. Bạn cho mình biết mục tiêu học và số điện thoại để tư vấn nhanh nhé.";

        await adapter.SendAsync(conversation.ExternalThreadId, reply, ct).ConfigureAwait(false);
        conversation.AppendMessage("out", "bot", reply, "text", clock.UtcNow, messageType: "comment", parentPostId: inbound.ParentPostId);

        await adapter.SendAsync(conversation.ExternalThreadId, dm, ct).ConfigureAwait(false);
        conversation.AppendMessage("out", "bot", dm, "text", clock.UtcNow, messageType: "dm", parentPostId: inbound.ParentPostId);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogCommentAutoReplySent(logger, conversation.Id, inbound.ParentPostId ?? string.Empty, detected.Label);
    }

    [LoggerMessage(EventId = 9201, Level = LogLevel.Information,
        Message = "Chat-2 comment auto-reply sent for conversation {ConversationId}, post {ParentPostId}, intent {IntentLabel}")]
    private static partial void LogCommentAutoReplySent(
        ILogger logger,
        Guid conversationId,
        string parentPostId,
        string intentLabel);
}
