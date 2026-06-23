using Clawbot.Domain.Common;

namespace Clawbot.Domain.Channels;

public sealed class InboxMember
{
    public Guid InboxId { get; private set; }
    public Guid AgentId { get; private set; }

    private InboxMember() { }

    public static InboxMember Create(Guid inboxId, Guid agentId)
    {
        return new InboxMember
        {
            InboxId = inboxId,
            AgentId = agentId,
        };
    }
}