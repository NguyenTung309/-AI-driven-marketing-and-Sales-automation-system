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
    // Chat-2: 'text' (DM/normal) | 'comment' (public comment under a post) | 'dm'. ParentPostId
    // links a comment to its post so the comment auto-reply + DM-invite flow can act on it.
    public string MessageType { get; private set; } = "text";
    public string? ParentPostId { get; private set; }
    public string? ExternalMessageId { get; private set; }
    public string? OriginalContent { get; private set; }
    public string? RedactedContent { get; private set; }
    public string? SenderDisplayName { get; private set; }
    public string? SenderAvatarUrl { get; private set; }
    public string? AttachmentUrl { get; private set; }
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
        Guid? senderUserId = null,
        string? externalMessageId = null,
        string? originalContent = null,
        string? redactedContent = null,
        string messageType = "text",
        string? parentPostId = null,
        string? senderDisplayName = null,
        string? senderAvatarUrl = null,
        string? attachmentUrl = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            TenantId = tenantId,
            Direction = direction,
            SenderType = senderType,
            SenderUserId = senderUserId,
            Content = content,
            ContentType = contentType,
            ExternalMessageId = externalMessageId,
            OriginalContent = originalContent,
            RedactedContent = redactedContent,
            MessageType = messageType,
            ParentPostId = parentPostId,
            SenderDisplayName = senderDisplayName,
            SenderAvatarUrl = senderAvatarUrl,
            AttachmentUrl = attachmentUrl,
            SentAt = sentAt,
        };
}
