using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Infrastructure.Channels.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using Clawbot.SharedKernel.Notifications;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class CommentAutoReplyJob(
    AppDbContext db,
    IIntentClassifier intent,
    IClock clock,
    INotificationPublisher publisher,
    ILogger<CommentAutoReplyJob> logger,
    ICommentChannelAdapterResolver adapterResolver)
{
    private const int MaxRepliesPerCustomerPerDay = 3;
    private const int MaxRepliesPerPostPerDay = 20;
    private const int MaxRepliesPerPostPerRun = 5;
    private const int MinPostReplyGapSeconds = 20;
    private const int MaxRepliesPerInboxPerDay = 200;
    private static readonly HashSet<string> ActionableLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "purchase_intent",
        "ask_price",
        "book_trial",
    };
    private static readonly string[] PublicReplyVariants =
    [
        "Cảm ơn bạn đã quan tâm. Mình đã nhắn riêng để tư vấn chi tiết ngay.",
        "Chào bạn, mình vừa gửi thông tin qua tin nhắn riêng nhé.",
        "Bạn kiểm tra tin nhắn giúp mình nhé, mình đã gửi chi tiết ở đó.",
        "Mình đã nhắn riêng cho bạn thông tin đầy đủ rồi nhé.",
    ];
    private readonly Dictionary<(Guid TenantId, string PostId), int> _batchPostReplies = [];

    // Đường polling: comment ingest qua bus consumer (chạy đa host, không enqueue Hangfire được) —
    // scan định kỳ quét comment mới rồi tái dùng RunAsync.
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
                // Cả lô dùng chung DbContext: xoá sạch tracker để một candidate hỏng
                // không kéo theo entity dở dang sang candidate sau.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task RunAsync(Guid tenantId, Guid messageId, CancellationToken ct)
    {
        var inbound = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (inbound is null || !string.Equals(inbound.MessageType, "comment", StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.IsNullOrWhiteSpace(inbound.ParentCommentId))
        {
            LogNestedCommentSkipped(logger, inbound.ConversationId, inbound.ParentCommentId);
            return;
        }

        var conversation = await db.Conversations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == inbound.ConversationId && c.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (conversation is null)
            return;

        var adapter = await adapterResolver.ResolveAsync(
            tenantId,
            conversation.Platform,
            conversation.ExternalThreadId,
            ct).ConfigureAwait(false);
        if (adapter is null)
        {
            LogNoCommentAdapter(logger, conversation.Id, conversation.Platform);
            return;
        }

        if (string.IsNullOrEmpty(inbound.ExternalMessageId))
        {
            LogMissingIds(logger, conversation.Id, "external_message_id");
            return;
        }

        var alreadyHandled = await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId
                && m.ParentCommentId == inbound.ExternalMessageId
                && m.Status != "send_failed", ct)
            .ConfigureAwait(false);
        if (alreadyHandled)
            return;

        // Sale đã trả lời trực tiếp comment này thì bot không chen vào. Các reply bot được
        // liên kết bằng ParentCommentId ở dưới nên không bị nhầm thành manual reply.
        var manuallyHandled = await db.Messages.IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId
                && m.ConversationId == conversation.Id
                && m.Direction == "out"
                && m.MessageType == "comment"
                && m.SenderType != "bot"
                && m.ParentPostId == inbound.ParentPostId
                && m.SentAt >= inbound.SentAt, ct)
            .ConfigureAwait(false);
        if (manuallyHandled)
            return;

        var text = inbound.OriginalContent ?? inbound.Content;
        var detected = await intent.ClassifyAsync(text, "vi-VN", ct).ConfigureAwait(false);
        if (detected.Confidence < 0.5f || !ActionableLabels.Contains(detected.Label))
            return;

        // Review-gate P3: tôn trọng manual-mode/handover và hội thoại đã đóng.
        if (!conversation.AiAutoReplyEnabled
            || string.Equals(conversation.Status, "resolved", StringComparison.OrdinalIgnoreCase))
        {
            LogCommentAutoReplySkipped(logger, conversation.Id, conversation.AiAutoReplyEnabled, conversation.Status);
            return;
        }

        var now = clock.UtcNow;
        var publicClaimResult = await ClaimPublicReplyAsync(
            tenantId,
            conversation,
            inbound,
            now,
            ct).ConfigureAwait(false);
        if (publicClaimResult is null)
        {
            LogReplyCapped(logger, conversation.Id, inbound.ParentPostId ?? string.Empty);
            return;
        }

        var publicClaim = publicClaimResult.Claim;
        var reply = PublicReplyVariants[publicClaimResult.VariantIndex];
        try
        {
            var replyExternalId = await adapter.SendCommentReplyAsync(
                conversation.TenantId,
                conversation.ExternalThreadId,
                inbound.ExternalMessageId,
                reply,
                ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(replyExternalId))
                publicClaim.SetExternalMessageId(replyExternalId);
            publicClaim.MarkSent();
        }
        catch (ChannelDeliveryAmbiguousException exception)
        {
            publicClaim.MarkOutcomeUnknown();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogPublicReplyAmbiguous(logger, conversation.Id, inbound.ExternalMessageId, exception);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            publicClaim.MarkSendFailed();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogPublicReplyFailed(logger, conversation.Id, inbound.ExternalMessageId, exception);
            return;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Meta private reply: one per comment and only within seven days. Pancake keeps its own
        // payload semantics; the same caps are harmless and prevent repeated DM invitations.
        var fromId = conversation.ContactId.HasValue
            ? await db.ContactExternalIds.IgnoreQueryFilters()
                .Where(x => x.ContactId == conversation.ContactId.Value && x.Platform == conversation.Platform)
                .Select(x => x.ExternalId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        var dm = "Chào bạn, mình gửi thông tin chi tiết tại đây. Bạn cho mình biết mục tiêu học và số điện thoại để tư vấn nhanh nhé.";
        if (inbound.SentAt < now.AddDays(-7))
        {
            LogPrivateReplySkipped(logger, conversation.Id, inbound.ExternalMessageId, "outside_7_day_window");
        }
        else if (string.IsNullOrEmpty(inbound.ParentPostId) || string.IsNullOrEmpty(fromId))
        {
            LogMissingIds(logger, conversation.Id, string.IsNullOrEmpty(inbound.ParentPostId) ? "post_id" : "from_id");
        }
        else if (await HasRecentPrivateReplyAsync(conversation, inbound, now, ct).ConfigureAwait(false))
        {
            LogPrivateReplySkipped(logger, conversation.Id, inbound.ExternalMessageId, "already_sent_or_capped");
        }
        else
        {
            var dmClaim = conversation.AppendMessage(
                "out",
                "bot",
                dm,
                "text",
                clock.UtcNow,
                messageType: "dm",
                parentPostId: inbound.ParentPostId,
                status: "pending_send",
                parentCommentId: inbound.ExternalMessageId);
            var dmClaimed = true;
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (IsCommentClaimConflict(exception))
            {
                dmClaimed = false;
                DiscardClaim(conversation, dmClaim);
            }
            catch
            {
                DiscardClaim(conversation, dmClaim);
                throw;
            }

            if (dmClaimed)
            {
                try
                {
                    var dmExternalId = await adapter.SendPrivateReplyAsync(
                        conversation.TenantId,
                        conversation.ExternalThreadId,
                        inbound.ParentPostId,
                        inbound.ExternalMessageId,
                        fromId,
                        dm,
                        ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(dmExternalId))
                        dmClaim.SetExternalMessageId(dmExternalId);
                    dmClaim.MarkSent();
                }
                catch (ChannelDeliveryAmbiguousException exception)
                {
                    dmClaim.MarkOutcomeUnknown();
                    LogPrivateReplyAmbiguous(logger, conversation.Id, inbound.ExternalMessageId, exception);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    dmClaim.MarkSendFailed();
                    LogPrivateReplyFailed(logger, ex, conversation.Id, inbound.ExternalMessageId);
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogCommentAutoReplySent(logger, conversation.Id, inbound.ParentPostId ?? string.Empty, detected.Label);
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

    // Các trần chống spam đều là đọc-rồi-ghi: nếu chỉ dựa vào unique index theo comment thì
    // nhiều worker/host vẫn cùng vượt trần theo post/khách/inbox. Applock theo post + transaction
    // biến bước kiểm tra trần và ghi claim thành một thao tác nguyên tử.
    private async Task<PublicReplyClaim?> ClaimPublicReplyAsync(
        Guid tenantId,
        Clawbot.Domain.Conversations.Conversation conversation,
        Clawbot.Domain.Conversations.Message inbound,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inbound.ParentPostId))
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquirePostReplyLockAsync(tenantId, inbound.ParentPostId, ct).ConfigureAwait(false);

        var variantIndex = await GetReplyVariantIndexAsync(tenantId, conversation, inbound, now, ct)
            .ConfigureAwait(false);
        if (variantIndex is null)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return null;
        }

        var claim = conversation.AppendMessage(
            "out",
            "bot",
            PublicReplyVariants[variantIndex.Value],
            "text",
            now,
            messageType: "comment",
            parentPostId: inbound.ParentPostId,
            status: "pending_send",
            parentCommentId: inbound.ExternalMessageId);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsCommentClaimConflict(exception))
        {
            DiscardClaim(conversation, claim);
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch
        {
            // RunScanAsync tái dùng một DbContext cho cả lô: claim còn treo Added sẽ bị
            // insert nhầm ở candidate kế tiếp.
            DiscardClaim(conversation, claim);
            throw;
        }

        var postKey = (tenantId, inbound.ParentPostId);
        _batchPostReplies[postKey] = _batchPostReplies.GetValueOrDefault(postKey) + 1;
        return new PublicReplyClaim(claim, variantIndex.Value);
    }

    private async Task AcquirePostReplyLockAsync(Guid tenantId, string parentPostId, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer())
            return;

        var resource = $"clawbot:comment-reply:{tenantId:N}:{parentPostId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource={resource},
                @LockMode='Exclusive',
                @LockOwner='Transaction',
                @LockTimeout=5000;
            IF @result < 0
                THROW 51092, 'comment_reply_lock_failed', 1;
            """, ct).ConfigureAwait(false);
    }

    private void DiscardClaim(
        Clawbot.Domain.Conversations.Conversation conversation,
        Clawbot.Domain.Conversations.Message claim)
    {
        conversation.DiscardMessage(claim);
        db.Entry(claim).State = EntityState.Detached;
    }

    private sealed record PublicReplyClaim(Clawbot.Domain.Conversations.Message Claim, int VariantIndex);

    private async Task<int?> GetReplyVariantIndexAsync(
        Guid tenantId,
        Clawbot.Domain.Conversations.Conversation conversation,
        Clawbot.Domain.Conversations.Message inbound,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inbound.ParentPostId))
            return null;
        var postKey = (tenantId, inbound.ParentPostId);
        if (_batchPostReplies.GetValueOrDefault(postKey) >= MaxRepliesPerPostPerRun)
            return null;

        var since = now.AddDays(-1);
        var recentPostReplies = db.Messages.IgnoreQueryFilters()
            .Where(message => message.TenantId == tenantId
                && message.Direction == "out"
                && message.SenderType == "bot"
                && message.MessageType == "comment"
                && message.Status != "send_failed"
                && message.ParentPostId == inbound.ParentPostId
                && message.SentAt >= since);
        if (await recentPostReplies.AnyAsync(message => message.ConversationId == conversation.Id, ct).ConfigureAwait(false))
            return null;
        if (conversation.ContactId.HasValue
            && await recentPostReplies.Join(
                    db.Conversations.IgnoreQueryFilters(),
                    message => message.ConversationId,
                    other => other.Id,
                    (message, other) => new { message, other })
                .AnyAsync(row => row.other.TenantId == tenantId && row.other.ContactId == conversation.ContactId, ct)
                .ConfigureAwait(false))
        {
            return null;
        }

        var customerDayCount = conversation.ContactId.HasValue
            ? await db.Messages.IgnoreQueryFilters()
                .Join(db.Conversations.IgnoreQueryFilters(), message => message.ConversationId, other => other.Id, (message, other) => new { message, other })
                .CountAsync(row => row.message.TenantId == tenantId
                    && row.other.TenantId == tenantId
                    && row.other.ContactId == conversation.ContactId
                    && row.message.Direction == "out"
                    && row.message.SenderType == "bot"
                    && row.message.MessageType == "comment"
                    && row.message.SentAt >= since, ct).ConfigureAwait(false)
            : 0;
        if (customerDayCount >= MaxRepliesPerCustomerPerDay)
            return null;

        var postCount = await recentPostReplies.CountAsync(ct).ConfigureAwait(false);
        if (postCount >= MaxRepliesPerPostPerDay)
            return null;
        var lastReplyAt = await recentPostReplies
            .OrderByDescending(message => message.SentAt)
            .Select(message => (DateTimeOffset?)message.SentAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (lastReplyAt.HasValue && now - lastReplyAt.Value < TimeSpan.FromSeconds(MinPostReplyGapSeconds))
            return null;

        if (conversation.InboxId.HasValue)
        {
            var inboxDayCount = await db.Messages.IgnoreQueryFilters()
                .Join(db.Conversations.IgnoreQueryFilters(), message => message.ConversationId, other => other.Id, (message, other) => new { message, other })
                .CountAsync(row => row.message.TenantId == tenantId
                    && row.other.TenantId == tenantId
                    && row.other.InboxId == conversation.InboxId
                    && row.message.Direction == "out"
                    && row.message.SenderType == "bot"
                    && row.message.MessageType == "comment"
                    && row.message.SentAt >= since, ct).ConfigureAwait(false);
            if (inboxDayCount >= MaxRepliesPerInboxPerDay)
                return null;
        }

        return postCount % PublicReplyVariants.Length;
    }

    private async Task<bool> HasRecentPrivateReplyAsync(
        Clawbot.Domain.Conversations.Conversation conversation,
        Clawbot.Domain.Conversations.Message inbound,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (await db.Messages.IgnoreQueryFilters().AnyAsync(message => message.TenantId == conversation.TenantId
                && message.ParentCommentId == inbound.ExternalMessageId
                && message.MessageType == "dm"
                && message.Status != "send_failed", ct).ConfigureAwait(false))
        {
            return true;
        }

        return await db.Messages.IgnoreQueryFilters().AnyAsync(message => message.TenantId == conversation.TenantId
            && message.ConversationId == conversation.Id
            && message.Direction == "out"
            && message.SenderType == "bot"
            && message.MessageType == "dm"
            && message.Status != "send_failed"
            && message.ParentPostId == inbound.ParentPostId
            && message.SentAt >= now.AddDays(-1), ct).ConfigureAwait(false);
    }

    private static bool IsCommentClaimConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && (sql.Number == 2601 || sql.Number == 2627)
        && sql.Message.Contains("UX_messages_bot_parent_comment_type", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 9201, Level = LogLevel.Information,
        Message = "Chat-2 comment auto-reply sent for conversation {ConversationId}, post {ParentPostId}, intent {IntentLabel}")]
    private static partial void LogCommentAutoReplySent(ILogger logger, Guid conversationId, string parentPostId, string intentLabel);

    [LoggerMessage(EventId = 9213, Level = LogLevel.Information,
        Message = "Chat-2 nested comment skipped for conversation {ConversationId}, parent comment {ParentCommentId}")]
    private static partial void LogNestedCommentSkipped(ILogger logger, Guid conversationId, string parentCommentId);

    [LoggerMessage(EventId = 9202, Level = LogLevel.Information,
        Message = "Chat-2 comment auto-reply skipped for conversation {ConversationId}: aiAutoReplyEnabled={AiAutoReplyEnabled}, status={Status}")]
    private static partial void LogCommentAutoReplySkipped(ILogger logger, Guid conversationId, bool aiAutoReplyEnabled, string status);

    [LoggerMessage(EventId = 9203, Level = LogLevel.Warning,
        Message = "Chat-2 comment auto-reply skipped for conversation {ConversationId}, platform {Platform}: no adapter")]
    private static partial void LogNoCommentAdapter(ILogger logger, Guid conversationId, string platform);

    [LoggerMessage(EventId = 9204, Level = LogLevel.Warning,
        Message = "Chat-2 comment auto-reply for conversation {ConversationId} missing {MissingField} — public reply/DM skipped accordingly")]
    private static partial void LogMissingIds(ILogger logger, Guid conversationId, string missingField);

    [LoggerMessage(EventId = 9205, Level = LogLevel.Warning,
        Message = "Chat-2 private reply failed for conversation {ConversationId}, comment {CommentId}")]
    private static partial void LogPrivateReplyFailed(ILogger logger, Exception ex, Guid conversationId, string commentId);

    [LoggerMessage(EventId = 9210, Level = LogLevel.Warning,
        Message = "Chat-2 public reply delivery ambiguous for conversation {ConversationId}, comment {CommentId}")]
    private static partial void LogPublicReplyAmbiguous(ILogger logger, Guid conversationId, string commentId, Exception ex);

    [LoggerMessage(EventId = 9211, Level = LogLevel.Warning,
        Message = "Chat-2 public reply failed for conversation {ConversationId}, comment {CommentId}")]
    private static partial void LogPublicReplyFailed(ILogger logger, Guid conversationId, string commentId, Exception ex);

    [LoggerMessage(EventId = 9212, Level = LogLevel.Warning,
        Message = "Chat-2 private reply delivery ambiguous for conversation {ConversationId}, comment {CommentId}")]
    private static partial void LogPrivateReplyAmbiguous(ILogger logger, Guid conversationId, string commentId, Exception ex);

    [LoggerMessage(EventId = 9206, Level = LogLevel.Warning,
        Message = "Chat-2 comment auto-reply candidate failed for conversation {ConversationId}")]
    private static partial void LogCandidateFailed(ILogger logger, Exception ex, Guid conversationId);

    [LoggerMessage(EventId = 9208, Level = LogLevel.Information,
        Message = "Chat-2 private reply skipped for conversation {ConversationId}, comment {CommentId}: {Reason}")]
    private static partial void LogPrivateReplySkipped(ILogger logger, Guid conversationId, string commentId, string reason);

    [LoggerMessage(EventId = 9209, Level = LogLevel.Warning,
        Message = "Chat-2 comment auto-reply capped for conversation {ConversationId}, post {ParentPostId}")]
    private static partial void LogReplyCapped(ILogger logger, Guid conversationId, string parentPostId);
}
