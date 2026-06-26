using Clawbot.Domain.Common;

namespace Clawbot.Domain.Channels;

public sealed class InboxMember : ITenantOwned
{
    public Guid InboxId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid TenantId { get; private set; }

    private InboxMember() { }

    public static InboxMember Create(Guid tenantId, Guid inboxId, Guid agentId)
    {
        return new InboxMember
        {
            TenantId = tenantId,
            InboxId = inboxId,
            AgentId = agentId,
        };
    }
}