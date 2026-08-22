using Clawbot.Domain.Leads.Events;
using Clawbot.Domain.Conversations.Events;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Events;

public sealed class DomainEventRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LeadId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    [Fact]
    public void LeadBecameCustomer_SetsAllFields()
    {
        var evt = new LeadBecameCustomer(TenantId, LeadId, OwnerId, 85, Now);

        evt.TenantId.Should().Be(TenantId);
        evt.LeadId.Should().Be(LeadId);
        evt.OwnerUserId.Should().Be(OwnerId);
        evt.Score.Should().Be(85);
        evt.OccurredOn.Should().Be(Now);
    }

    [Fact]
    public void LeadBecameHot_SetsAllFields()
    {
        var evt = new LeadBecameHot(TenantId, LeadId, OwnerId, 72, Now);

        evt.Score.Should().Be(72);
        evt.OwnerUserId.Should().Be(OwnerId);
    }

    [Fact]
    public void LeadBecameWarm_SetsAllFields()
    {
        var evt = new LeadBecameWarm(TenantId, LeadId, 45, Now);

        evt.Score.Should().Be(45);
        evt.OccurredOn.Should().Be(Now);
    }

    [Fact]
    public void LeadCreated_SetsAllFields()
    {
        var contactId = Guid.NewGuid();
        var evt = new LeadCreated(TenantId, LeadId, contactId, "facebook", Now);

        evt.ContactId.Should().Be(contactId);
        evt.SourcePlatform.Should().Be("facebook");
    }

    [Fact]
    public void LeadReactivated_SetsAllFields()
    {
        var evt = new LeadReactivated(TenantId, LeadId, null, 30, Now);

        evt.OwnerUserId.Should().BeNull();
        evt.Score.Should().Be(30);
    }

    [Fact]
    public void ConversationEscalated_SetsAllFields()
    {
        var convId = Guid.NewGuid();
        var evt = new ConversationEscalated(TenantId, convId, Now);

        evt.TenantId.Should().Be(TenantId);
        evt.ConversationId.Should().Be(convId);
        evt.OccurredOn.Should().Be(Now);
    }
}
