using Clawbot.SharedKernel.Inbox;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Inbox;

public sealed class InboxMessageEventTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 8, 17, 10, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Constructor_SetsRequiredFields()
    {
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var evt = new InboxMessageEvent(
            conversationId,
            messageId,
            "inbound",
            "customer",
            "Học phí bao nhiêu?",
            "text",
            SentAt);

        evt.ConversationId.Should().Be(conversationId);
        evt.MessageId.Should().Be(messageId);
        evt.Direction.Should().Be("inbound");
        evt.SenderType.Should().Be("customer");
        evt.Content.Should().Be("Học phí bao nhiêu?");
        evt.ContentType.Should().Be("text");
        evt.SentAt.Should().Be(SentAt);
    }

    [Fact]
    public void Optionals_DefaultToNullOrFalse()
    {
        var evt = new InboxMessageEvent(
            Guid.NewGuid(), Guid.NewGuid(), "inbound", "customer", "x", "text", SentAt);

        evt.AssignedTo.Should().BeNull();
        evt.SenderDisplayName.Should().BeNull();
        evt.SenderAvatarUrl.Should().BeNull();
        evt.InboxId.Should().BeNull();
        evt.IsSynthetic.Should().BeFalse();
        evt.ConversationStatus.Should().BeNull();
    }

    [Fact]
    public void ConversationStatus_CarriesPostWriteState()
    {
        var evt = new InboxMessageEvent(
            Guid.NewGuid(), Guid.NewGuid(), "inbound", "customer", "x", "text", SentAt,
            ConversationStatus: "open");

        evt.ConversationStatus.Should().Be("open");
    }
}

public sealed class InboxMessageStatusEventTests
{
    [Fact]
    public void Constructor_SetsFieldsAndDefaults()
    {
        var conversationId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var evt = new InboxMessageStatusEvent(conversationId, messageId, "delivered");

        evt.ConversationId.Should().Be(conversationId);
        evt.MessageId.Should().Be(messageId);
        evt.Status.Should().Be("delivered");
        evt.AssignedTo.Should().BeNull();
        evt.InboxId.Should().BeNull();
    }
}

public sealed class InboxConversationEventTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var conversationId = Guid.NewGuid();
        var assignedTo = Guid.NewGuid();
        var lastMessageAt = DateTimeOffset.UnixEpoch;

        var evt = new InboxConversationEvent(conversationId, "resolved", assignedTo, lastMessageAt);

        evt.ConversationId.Should().Be(conversationId);
        evt.Status.Should().Be("resolved");
        evt.AssignedTo.Should().Be(assignedTo);
        evt.LastMessageAt.Should().Be(lastMessageAt);
        evt.InboxId.Should().BeNull();
    }
}

public sealed class InboxTypingEventTests
{
    [Fact]
    public void Constructor_DefaultsSourceToAi()
    {
        var evt = new InboxTypingEvent(Guid.NewGuid(), IsTyping: true);

        evt.IsTyping.Should().BeTrue();
        evt.Source.Should().Be("ai");
        evt.AssignedTo.Should().BeNull();
        evt.InboxId.Should().BeNull();
    }

    [Fact]
    public void WithExpression_TogglesTypingWithoutMutating()
    {
        var start = new InboxTypingEvent(Guid.NewGuid(), IsTyping: true);
        var stop = start with { IsTyping = false };

        start.IsTyping.Should().BeTrue();
        stop.IsTyping.Should().BeFalse();
        stop.ConversationId.Should().Be(start.ConversationId);
    }
}
