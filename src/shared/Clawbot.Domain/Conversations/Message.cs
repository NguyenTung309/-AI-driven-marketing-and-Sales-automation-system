using Clawbot.Domain.Common;

namespace Clawbot.Domain.Conversations;

public sealed class Message : Entity<Guid>, ITenantOwned
{
    public Guid ConversationId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Direction { get; private set; } = string.Empty;       // in|out
    public string SenderType { get; private set; } = string.Empty;      // contact|user|agent|system
    public Guid? SenderUserId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "text";
    public DateTimeOffset SentAt { get; private set; }

    private Message() { }

    internal static Message Create(
        Guid conversationId,
        Guid tenantId,
        string direction,
        string senderType,
        string content,
        string contentType,
        DateTimeOffset sentAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TenantId = tenantId,
            Direction = direction,
            SenderType = senderType,
            Content = content,
            ContentType = contentType,
            SentAt = sentAt,
        };
}
