using Clawbot.Domain.Notifications;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Notification CreateDefault() => Notification.Create(
        TenantId, UserId, "job_completed", "Job done", Now,
        severity: "success", body: "Content generated", link: "/jobs/123", groupKey: "job-123");

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields()
    {
        var n = CreateDefault();

        n.TenantId.Should().Be(TenantId);
        n.UserId.Should().Be(UserId);
        n.Type.Should().Be("job_completed");
        n.Title.Should().Be("Job done");
        n.Severity.Should().Be("success");
        n.Body.Should().Be("Content generated");
        n.Link.Should().Be("/jobs/123");
        n.IsRead.Should().BeFalse();
        n.ReadAt.Should().BeNull();
        n.CreatedAt.Should().Be(Now);
        n.GroupKey.Should().Be("job-123");
        n.OccurrenceCount.Should().Be(1);
        n.LastOccurredAt.Should().Be(Now);
        n.EmailSentAt.Should().BeNull();
    }

    [Fact]
    public void Create_AllowsNullUserIdForBroadcast()
    {
        var n = Notification.Create(TenantId, null, "system_alert", "Alert", Now);

        n.UserId.Should().BeNull();
    }

    [Fact]
    public void Create_DefaultsSeverityToInfo()
    {
        var n = Notification.Create(TenantId, UserId, "test", "Test", Now);

        n.Severity.Should().Be("info");
    }

    // ── Bump ──────────────────────────────────────────────────────────

    [Fact]
    public void Bump_IncrementsOccurrenceAndUpdatesLastOccurred()
    {
        var n = CreateDefault();

        n.Bump(Now.AddMinutes(5), "Updated body");

        n.OccurrenceCount.Should().Be(2);
        n.LastOccurredAt.Should().Be(Now.AddMinutes(5));
        n.Body.Should().Be("Updated body");
    }

    [Fact]
    public void Bump_PreservesBodyWhenNewBodyEmpty()
    {
        var n = CreateDefault();

        n.Bump(Now.AddMinutes(5), null);

        n.OccurrenceCount.Should().Be(2);
        n.Body.Should().Be("Content generated");
    }

    [Fact]
    public void Bump_PreservesBodyWhenNewBodyIsEmptyString()
    {
        var n = CreateDefault();

        n.Bump(Now.AddMinutes(5), "");

        n.Body.Should().Be("Content generated");
    }

    // ── MarkEmailSent ─────────────────────────────────────────────────

    [Fact]
    public void MarkEmailSent_SetsTimestamp()
    {
        var n = CreateDefault();

        n.MarkEmailSent(Now.AddMinutes(10));

        n.EmailSentAt.Should().Be(Now.AddMinutes(10));
    }

    // ── MarkRead ──────────────────────────────────────────────────────

    [Fact]
    public void MarkRead_SetsIsReadAndReadAt()
    {
        var n = CreateDefault();

        n.MarkRead(Now.AddMinutes(5));

        n.IsRead.Should().BeTrue();
        n.ReadAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MarkRead_NoOpWhenAlreadyRead()
    {
        var n = CreateDefault();
        n.MarkRead(Now.AddMinutes(5));

        n.MarkRead(Now.AddMinutes(10));

        n.ReadAt.Should().Be(Now.AddMinutes(5));
    }
}
