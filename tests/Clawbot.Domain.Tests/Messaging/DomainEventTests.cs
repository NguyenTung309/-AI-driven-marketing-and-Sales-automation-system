using Clawbot.Domain.Conversations;
using Clawbot.Domain.Conversations.Events;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Leads.Events;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Messaging;

public sealed class DomainEventTests
{
    [Fact]
    public void Lead_create_raises_LeadCreated()
    {
        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var lead = Lead.Create(tenantId, contactId, "facebook", DateTimeOffset.UtcNow);

        var evt = lead.DomainEvents.Should().ContainSingle().Which
            .Should().BeOfType<LeadCreated>().Subject;
        evt.TenantId.Should().Be(tenantId);
        evt.LeadId.Should().Be(lead.Id);
        evt.ContactId.Should().Be(contactId);
        evt.SourcePlatform.Should().Be("facebook");
    }

    [Fact]
    public void Conversation_escalate_raises_ConversationEscalated()
    {
        var conv = Conversation.Open(Guid.NewGuid(), "zalo", "thread-1", DateTimeOffset.UtcNow);
        conv.ClearDomainEvents();

        conv.Escalate();

        var evt = conv.DomainEvents.Should().ContainSingle().Which
            .Should().BeOfType<ConversationEscalated>().Subject;
        evt.ConversationId.Should().Be(conv.Id);
    }

    [Fact]
    public void ClearDomainEvents_empties_the_list()
    {
        var lead = Lead.Create(Guid.NewGuid(), Guid.NewGuid(), "tiktok", DateTimeOffset.UtcNow);
        lead.ClearDomainEvents();
        lead.DomainEvents.Should().BeEmpty();
    }
}
