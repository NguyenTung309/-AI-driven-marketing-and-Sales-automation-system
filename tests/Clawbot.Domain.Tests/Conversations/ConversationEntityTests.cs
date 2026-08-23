using Clawbot.Domain.Conversations;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Conversations;

public sealed class ConversationLabelTests
{
    [Fact]
    public void Create_SetsFieldsAndTimestamp()
    {
        var convId = Guid.NewGuid();
        var labelId = Guid.NewGuid();

        var cl = ConversationLabel.Create(convId, labelId);

        cl.ConversationId.Should().Be(convId);
        cl.LabelId.Should().Be(labelId);
        cl.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}

public sealed class ConversationNoteTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ConvId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var note = ConversationNote.Create(TenantId, ConvId, UserId, "Nội dung ghi chú", "Admin User", "internal");

        note.TenantId.Should().Be(TenantId);
        note.ConversationId.Should().Be(ConvId);
        note.CreatedByUserId.Should().Be(UserId);
        note.CreatedByDisplayName.Should().Be("Admin User");
        note.Content.Should().Be("Nội dung ghi chú");
        note.Type.Should().Be("internal");
        note.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        note.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_DefaultType_IsPrivate()
    {
        var note = ConversationNote.Create(TenantId, ConvId, UserId, "content", null);

        note.Type.Should().Be("private");
        note.CreatedByDisplayName.Should().BeNull();
    }

    [Fact]
    public void UpdateContent_ChangesContentAndTimestamp()
    {
        var note = ConversationNote.Create(TenantId, ConvId, UserId, "old", null);

        note.UpdateContent("new content");

        note.Content.Should().Be("new content");
        note.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
