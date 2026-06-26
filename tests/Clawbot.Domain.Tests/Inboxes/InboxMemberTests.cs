using Clawbot.Domain.Channels;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Inboxes;

public sealed class InboxMemberTests
{
    [Fact]
    public void Create_sets_inboxId_and_agentId()
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
