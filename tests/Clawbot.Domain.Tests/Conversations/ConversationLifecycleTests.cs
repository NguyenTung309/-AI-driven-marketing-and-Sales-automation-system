using Clawbot.Domain.Conversations;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Conversations;

public sealed class ConversationLifecycleTests
{
    [Fact]
    public void Open_creates_conversation_with_default_status()
    {
        var conv = Conversation.Open(Guid.NewGuid(), "facebook", "ext123", DateTimeOffset.UtcNow);
        conv.Status.Should().Be("open");
    }

    [Fact]
    public void ReopenIfNeeded_does_nothing_when_already_open()
    {
        var conv = Conversation.Open(Guid.NewGuid(), "facebook", "ext1", DateTimeOffset.UtcNow);

        conv.ReopenIfNeeded();

        conv.Status.Should().Be("open");
    }

    [Fact]
    public void ReopenIfNeeded_reopens_resolved_conversation()
    {
        var conv = Conversation.Open(Guid.NewGuid(), "facebook", "ext1", DateTimeOffset.UtcNow);
        conv.Resolve();
        conv.Status.Should().Be("resolved");

        conv.ReopenIfNeeded();

        conv.Status.Should().Be("open");
    }

    [Fact]
    public void ReopenIfNeeded_reopens_snoozed_conversation()
    {
        var conv = Conversation.Open(Guid.NewGuid(), "facebook", "ext1", DateTimeOffset.UtcNow);
        conv.Snooze(DateTimeOffset.UtcNow.AddHours(2));
        conv.Status.Should().Be("snoozed");

        conv.ReopenIfNeeded();

        conv.Status.Should().Be("open");
        conv.SnoozedUntil.Should().BeNull();
    }

    [Fact]
    public void ReopenIfNeeded_preserves_AssignedTo()
    {
        var saleId = Guid.NewGuid();
        var conv = Conversation.Open(Guid.NewGuid(), "facebook", "ext1", DateTimeOffset.UtcNow);
        conv.Assign(saleId);
        conv.Resolve();

        conv.ReopenIfNeeded();

        conv.AssignedTo.Should().Be(saleId);
    }

    [Fact]
    public void Snooze_sets_status_and_SnoozedUntil()
    {
        var conv = Conversation.Open(Guid.NewGuid(), "facebook", "ext1", DateTimeOffset.UtcNow);
        var until = DateTimeOffset.UtcNow.AddHours(4);

        conv.Snooze(until);

        conv.Status.Should().Be("snoozed");
        conv.SnoozedUntil.Should().Be(until);
    }

    [Fact]
    public void ReopenIfNeeded_clears_SnoozedUntil()
    {
        var conv = Conversation.Open(Guid.NewGuid(), "facebook", "ext1", DateTimeOffset.UtcNow);
        conv.Snooze(DateTimeOffset.UtcNow.AddHours(2));
        conv.SnoozedUntil.Should().NotBeNull();

        conv.ReopenIfNeeded();

        conv.SnoozedUntil.Should().BeNull();
    }
}
