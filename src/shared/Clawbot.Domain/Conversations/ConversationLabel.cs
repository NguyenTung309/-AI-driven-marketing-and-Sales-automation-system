namespace Clawbot.Domain.Conversations;

public sealed class ConversationLabel
{
    public Guid ConversationId { get; private set; }
    public Guid LabelId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ConversationLabel() { }

    public static ConversationLabel Create(Guid conversationId, Guid labelId)
        => new()
        {
            ConversationId = conversationId,
            LabelId = labelId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}