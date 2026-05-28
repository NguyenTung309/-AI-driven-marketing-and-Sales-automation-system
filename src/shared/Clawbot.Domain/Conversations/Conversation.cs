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
    public Guid? AssignedTo { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    private Conversation() { }

    public static Conversation Open(
        Guid tenantId,
        string platform,
        string externalThreadId,
        DateTimeOffset createdAt,
        Guid? contactId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            Platform = platform,
            ExternalThreadId = externalThreadId,
            CreatedAt = createdAt,
        };

    public void Assign(Guid userId) => AssignedTo = userId;

    public void Resolve() => Status = "resolved";
    public void Escalate() => Status = "escalated";

    public Message AppendMessage(string direction, string senderType, string content, string contentType, DateTimeOffset sentAt)
    {
        var msg = Message.Create(Id, TenantId, direction, senderType, content, contentType, sentAt);
        _messages.Add(msg);
        LastMessageAt = sentAt;
        return msg;
    }
}
