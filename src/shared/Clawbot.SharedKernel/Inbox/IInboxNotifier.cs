namespace Clawbot.SharedKernel.Inbox;

public interface IInboxNotifier
{
    Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default);
    Task NotifyMessageStatusAsync(Guid tenantId, InboxMessageStatusEvent evt, CancellationToken ct = default);
    Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default);
    Task NotifyTypingAsync(Guid tenantId, InboxTypingEvent evt, CancellationToken ct = default);
}

public sealed record InboxMessageEvent(
    Guid ConversationId,
    Guid MessageId,
    string Direction,
    string SenderType,
    string Content,
    string ContentType,
    DateTimeOffset SentAt,
    Guid? AssignedTo = null,
    string? SenderDisplayName = null,
    string? SenderAvatarUrl = null,
    Guid? InboxId = null,
    bool IsSynthetic = false,
    // Trạng thái hội thoại SAU khi ghi tin nhắn. FE dùng để biết hội thoại vừa được mở lại
    // (resolved/snoozed -> open) mà không phải gọi lại API. Null = producer cũ chưa gửi kèm.
    string? ConversationStatus = null);

public sealed record InboxMessageStatusEvent(
    Guid ConversationId,
    Guid MessageId,
    string Status,
    Guid? AssignedTo = null,
    Guid? InboxId = null);

public sealed record InboxConversationEvent(
    Guid ConversationId,
    string Status,
    Guid? AssignedTo,
    DateTimeOffset? LastMessageAt,
    Guid? InboxId = null);

// IsTyping=true khi AI bắt đầu sinh auto-reply, false khi đã lưu tin/thất bại — FE hiện bong bóng
// "AI đang soạn phản hồi". Source hiện chỉ có "ai"; để mở cho typing của sale sau này.
public sealed record InboxTypingEvent(
    Guid ConversationId,
    bool IsTyping,
    string Source = "ai",
    Guid? AssignedTo = null,
    Guid? InboxId = null);
