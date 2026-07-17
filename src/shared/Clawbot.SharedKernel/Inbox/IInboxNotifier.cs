namespace Clawbot.SharedKernel.Inbox;

public interface IInboxNotifier
{
    Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default);
    Task NotifyMessageStatusAsync(Guid tenantId, InboxMessageStatusEvent evt, CancellationToken ct = default);
    Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default);
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
    bool IsSynthetic = false);

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
