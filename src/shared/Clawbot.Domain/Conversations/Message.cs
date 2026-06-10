using Clawbot.Domain.Common;

namespace Clawbot.Domain.Conversations;

public sealed class Message : Entity<Guid>, ITenantOwned
{
    public Guid ConversationId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Direction { get; private set; } = string.Empty;
    public string SenderType { get; private set; } = string.Empty;
    public Guid? SenderUserId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "text";
    public string? ExternalMessageId { get; private set; }
    public string? OriginalContent { get; private set; }
    public string? RedactedContent { get; private set; }
    public DateTimeOffset SentAt { get; private set; }

    private Message() { }

    internal static Message Create(
        Guid conversationId,
        Guid tenantId,
        string direction,
        string senderType,
        string content,
        string contentType,
        DateTimeOffset sentAt,
        string? externalMessageId = null,
        string? originalContent = null,
        string? redactedContent = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TenantId = tenantId,
            Direction = direction,
            SenderType = senderType,
            Content = content,
            ContentType = contentType,
            ExternalMessageId = externalMessageId,
            OriginalContent = originalContent,
            RedactedContent = redactedContent,
            SentAt = sentAt,
        };
}
