using Clawbot.Domain.Conversations;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Conversations;

public sealed class ConversationNoteTests
{
    [Fact]
    public void Create_sets_properties()
    {
        var tenantId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var note = ConversationNote.Create(tenantId, convId, userId, "Ghi chu test", "Sale A", "private");

        note.TenantId.Should().Be(tenantId);
        note.ConversationId.Should().Be(convId);
        note.CreatedByUserId.Should().Be(userId);
        note.CreatedByDisplayName.Should().Be("Sale A");
        note.Content.Should().Be("Ghi chu test");
        note.Type.Should().Be("private");
    }

    [Fact]
    public void Create_uses_default_type_when_not_specified()
    {
        var note = ConversationNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "noi dung", null);

        note.Type.Should().Be("private");
    }
}
