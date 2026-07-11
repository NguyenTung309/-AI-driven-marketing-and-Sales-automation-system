using Clawbot.Domain.Common;

namespace Clawbot.Domain.Conversations;

public sealed class Conversation : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<Message> _messages = new();

    public Guid TenantId { get; private set; }
    public Guid? ContactId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string ExternalThreadId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "open";
    // Co "AI dang chat": bat -> tin inbound duoc AI auto-reply. Tat khi sale nhay vao (gui tay/escalate).
    public bool AiAutoReplyEnabled { get; private set; } = true;
    public Guid? AssignedTo { get; private set; }
    public DateTimeOffset? SnoozedUntil { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public Guid? InboxId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public byte[]? RowVersion { get; private set; }

    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    private Conversation() { }

    public static Conversation Open(
        Guid tenantId,
        string platform,
        string externalThreadId,
        DateTimeOffset createdAt,
        Guid? contactId = null,
        Guid? inboxId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            Platform = platform,
            ExternalThreadId = externalThreadId,
            CreatedAt = createdAt,
            InboxId = inboxId,
        };

    public void SetInboxId(Guid inboxId) => InboxId = inboxId;

    public void Assign(Guid userId) => AssignedTo = userId;

    public void Resolve() => Status = "resolved";

    public void Escalate()
    {
        Status = "escalated";
        AiAutoReplyEnabled = false;
        Raise(new Events.ConversationEscalated(TenantId, Id, DateTimeOffset.UtcNow));
    }

    public void SetAiAutoReply(bool enabled) => AiAutoReplyEnabled = enabled;

    public Message AppendMessage(string direction, string senderType, string content, string contentType, DateTimeOffset sentAt, Guid? senderUserId = null, string? externalMessageId = null, string? originalContent = null, string? redactedContent = null, string messageType = "text", string? parentPostId = null, string? senderDisplayName = null, string? senderAvatarUrl = null, string? attachmentUrl = null, string status = "sent")
    {
        var msg = Message.Create(Id, TenantId, direction, senderType, content, contentType, sentAt, senderUserId, externalMessageId, originalContent, redactedContent, messageType, parentPostId, senderDisplayName, senderAvatarUrl, attachmentUrl, status);
        _messages.Add(msg);
        LastMessageAt = sentAt;
        return msg;
    }

    public void Unassign() => AssignedTo = null;

    public void ReopenIfNeeded()
    {
        if (Status != "snoozed" && Status != "resolved") return;
        Status = "open";
        SnoozedUntil = null;
    }

    public void Snooze(DateTimeOffset until)
    {
        Status = "snoozed";
        SnoozedUntil = until;
    }

}





