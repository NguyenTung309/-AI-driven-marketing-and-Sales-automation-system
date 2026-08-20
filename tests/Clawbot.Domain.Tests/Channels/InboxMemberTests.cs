using Clawbot.Domain.Channels;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Channels;

public sealed class InboxMemberTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        var member = InboxMember.Create(tenantId, inboxId, agentId);

        member.TenantId.Should().Be(tenantId);
        member.InboxId.Should().Be(inboxId);
        member.AgentId.Should().Be(agentId);
    }
}
