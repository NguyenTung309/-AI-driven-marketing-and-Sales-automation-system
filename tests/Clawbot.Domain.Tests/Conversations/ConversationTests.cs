using Clawbot.Domain.Conversations;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Conversations;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ContactId = Guid.NewGuid();
    private static readonly Guid InboxId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Conversation CreateOpen() =>
        Conversation.Open(TenantId, "facebook", "thread-123", Now, ContactId, InboxId);

    // ── Open ──────────────────────────────────────────────────────────

    [Fact]
    public void Open_SetsInitialDefaults()
    {
        var conv = CreateOpen();

        conv.TenantId.Should().Be(TenantId);
        conv.ContactId.Should().Be(ContactId);
        conv.Platform.Should().Be("facebook");
        conv.ExternalThreadId.Should().Be("thread-123");
        conv.Status.Should().Be("open");
        conv.AiAutoReplyEnabled.Should().BeTrue();
        conv.AiAutoReplyResumeAt.Should().BeNull();
        conv.AssignedTo.Should().BeNull();
        conv.SnoozedUntil.Should().BeNull();
        conv.LastMessageAt.Should().BeNull();
        conv.MemoryExtractedAt.Should().BeNull();
        conv.InboxId.Should().Be(InboxId);
        conv.CreatedAt.Should().Be(Now);
        conv.DeletedAt.Should().BeNull();
        conv.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Open_AllowsNullContactAndInbox()
    {
        var conv = Conversation.Open(TenantId, "zalo", "t-1", Now);

        conv.ContactId.Should().BeNull();
        conv.InboxId.Should().BeNull();
    }

    // ── SetInboxId ────────────────────────────────────────────────────

    [Fact]
    public void SetInboxId_UpdatesInboxId()
    {
        var conv = CreateOpen();
        var newInbox = Guid.NewGuid();

        conv.SetInboxId(newInbox);

        conv.InboxId.Should().Be(newInbox);
    }

    // ── Assign / Unassign ─────────────────────────────────────────────

    [Fact]
    public void Assign_SetsAssignedTo()
    {
        var conv = CreateOpen();

        conv.Assign(UserId);

        conv.AssignedTo.Should().Be(UserId);
    }

    [Fact]
    public void Unassign_ClearsAssignedTo()
    {
        var conv = CreateOpen();
        conv.Assign(UserId);

        conv.Unassign();

        conv.AssignedTo.Should().BeNull();
    }

    // ── Resolve / ReopenIfNeeded ──────────────────────────────────────

    [Fact]
    public void Resolve_SetsStatusResolved()
    {
        var conv = CreateOpen();

        conv.Resolve();

        conv.Status.Should().Be("resolved");
    }

    [Fact]
    public void ReopenIfNeeded_FromResolved_ReopensToOpen()
    {
        var conv = CreateOpen();
        conv.Resolve();

        conv.ReopenIfNeeded();

        conv.Status.Should().Be("open");
        conv.SnoozedUntil.Should().BeNull();
    }

    [Fact]
    public void ReopenIfNeeded_FromSnoozed_ReopensToOpen()
    {
        var conv = CreateOpen();
        conv.Snooze(Now.AddHours(1));

        conv.ReopenIfNeeded();

        conv.Status.Should().Be("open");
        conv.SnoozedUntil.Should().BeNull();
    }

    [Fact]
    public void ReopenIfNeeded_NoOpWhenAlreadyOpen()
    {
        var conv = CreateOpen();

        conv.ReopenIfNeeded();

        conv.Status.Should().Be("open");
    }

    // ── Snooze ────────────────────────────────────────────────────────

    [Fact]
    public void Snooze_SetsStatusAndUntil()
    {
        var conv = CreateOpen();
        var until = Now.AddHours(2);

        conv.Snooze(until);

        conv.Status.Should().Be("snoozed");
        conv.SnoozedUntil.Should().Be(until);
    }

    // ── Escalate ──────────────────────────────────────────────────────

    [Fact]
    public void Escalate_DisablesAiAndSetsStatus()
    {
        var conv = CreateOpen();

        conv.Escalate();

        conv.Status.Should().Be("escalated");
        conv.AiAutoReplyEnabled.Should().BeFalse();
        conv.AiAutoReplyResumeAt.Should().BeNull();
    }

    // ── AI Auto-Reply toggle ──────────────────────────────────────────

    [Fact]
    public void SetAiAutoReply_EnableClearsResumeAt()
    {
        var conv = CreateOpen();
        conv.PauseAiAutoReplyUntil(Now.AddMinutes(30));

        conv.SetAiAutoReply(true);

        conv.AiAutoReplyEnabled.Should().BeTrue();
        conv.AiAutoReplyResumeAt.Should().BeNull();
    }

    [Fact]
    public void SetAiAutoReply_DisableClearsResumeAt()
    {
        var conv = CreateOpen();
        conv.PauseAiAutoReplyUntil(Now.AddMinutes(30));

        conv.SetAiAutoReply(false);

        conv.AiAutoReplyEnabled.Should().BeFalse();
        conv.AiAutoReplyResumeAt.Should().BeNull();
    }

    [Fact]
    public void PauseAiAutoReplyUntil_DisablesAndSetsResumeAt()
    {
        var conv = CreateOpen();
        var resumeAt = Now.AddMinutes(10);

        conv.PauseAiAutoReplyUntil(resumeAt);

        conv.AiAutoReplyEnabled.Should().BeFalse();
        conv.AiAutoReplyResumeAt.Should().Be(resumeAt);
    }

    // ── TryResumeAiAutoReply ──────────────────────────────────────────

    [Fact]
    public void TryResumeAiAutoReply_ResumesAfterResumeAt()
    {
        var conv = CreateOpen();
        conv.PauseAiAutoReplyUntil(Now.AddMinutes(5));

        var resumed = conv.TryResumeAiAutoReply(Now.AddMinutes(10));

        resumed.Should().BeTrue();
        conv.AiAutoReplyEnabled.Should().BeTrue();
        conv.AiAutoReplyResumeAt.Should().BeNull();
    }

    [Fact]
    public void TryResumeAiAutoReply_ReturnsFalseBeforeResumeAt()
    {
        var conv = CreateOpen();
        conv.PauseAiAutoReplyUntil(Now.AddMinutes(10));

        var resumed = conv.TryResumeAiAutoReply(Now.AddMinutes(5));

        resumed.Should().BeFalse();
        conv.AiAutoReplyEnabled.Should().BeFalse();
    }

    [Fact]
    public void TryResumeAiAutoReply_ReturnsFalseWhenAlreadyEnabled()
    {
        var conv = CreateOpen();

        var resumed = conv.TryResumeAiAutoReply(Now.AddMinutes(10));

        resumed.Should().BeFalse();
    }

    [Fact]
    public void TryResumeAiAutoReply_ReturnsFalseWhenNoResumeAtSet()
    {
        var conv = CreateOpen();
        conv.SetAiAutoReply(false);

        var resumed = conv.TryResumeAiAutoReply(Now.AddMinutes(10));

        resumed.Should().BeFalse();
    }

    // ── MarkMemoryExtracted ───────────────────────────────────────────

    [Fact]
    public void MarkMemoryExtracted_SetsTimestamp()
    {
        var conv = CreateOpen();

        conv.MarkMemoryExtracted(Now.AddMinutes(5));

        conv.MemoryExtractedAt.Should().Be(Now.AddMinutes(5));
    }

    // ── AppendMessage / DiscardMessage ────────────────────────────────

    [Fact]
    public void AppendMessage_AddsMessageAndUpdatesLastMessageAt()
    {
        var conv = CreateOpen();

        var msg = conv.AppendMessage("inbound", "customer", "Hello!", "text", Now.AddMinutes(1));

        conv.Messages.Should().ContainSingle();
        conv.LastMessageAt.Should().Be(Now.AddMinutes(1));
        msg.Content.Should().Be("Hello!");
        msg.Direction.Should().Be("inbound");
    }

    [Fact]
    public void DiscardMessage_RemovesFromCollection()
    {
        var conv = CreateOpen();
        var msg = conv.AppendMessage("outbound", "ai", "Hi", "text", Now);

        conv.DiscardMessage(msg);

        conv.Messages.Should().BeEmpty();
    }
}
