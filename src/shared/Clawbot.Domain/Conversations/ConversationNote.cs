namespace Clawbot.Domain.Conversations;

using Clawbot.Domain.Common;

public sealed class ConversationNote : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string? CreatedByDisplayName { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string Type { get; private set; } = "private";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ConversationNote() { }

    public static ConversationNote Create(
        Guid tenantId, Guid conversationId, Guid userId,
        string content, string? createdByName, string type = "private")
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            CreatedByUserId = userId,
            CreatedByDisplayName = createdByName,
            Content = content,
            Type = type,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    public void UpdateContent(string content)
    {
        Content = content;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}