namespace Clawbot.SharedKernel.Inbox;

public interface IInboxNotifier
{
    Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default);
    Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default);
}

public sealed record InboxMessageEvent(
    Guid ConversationId,
    Guid MessageId,
    string Direction,
    string SenderType,
    string Content,
    string ContentType,
    DateTimeOffset SentAt);

public sealed record InboxConversationEvent(
    Guid ConversationId,
    string Status,
    Guid? AssignedTo,
    DateTimeOffset? LastMessageAt);
