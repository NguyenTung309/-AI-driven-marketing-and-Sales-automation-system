using Clawbot.Domain.Common;

namespace Clawbot.Domain.Conversations;

public sealed class Conversation : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Channel { get; private set; } = string.Empty;
    public string ExternalThreadId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private Conversation() { }

    public static Conversation Open(Guid tenantId, string channel, string externalThreadId, DateTimeOffset createdAt)
    {
        return new Conversation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel,
            ExternalThreadId = externalThreadId,
            CreatedAt = createdAt,
        };
    }
}
