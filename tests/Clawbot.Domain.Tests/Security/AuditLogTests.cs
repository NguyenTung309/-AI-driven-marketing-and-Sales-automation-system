using System.Net;
using Clawbot.Domain.Security;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Security;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ResourceId = Guid.NewGuid();

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields()
    {
        var ip = IPAddress.Parse("192.168.1.1");
        var log = AuditLog.Create(TenantId, UserId, "update", "Lead", ResourceId, Now,
            diffJson: "{\"old\":\"a\",\"new\":\"b\"}", ip: ip, userAgent: "Mozilla/5.0");

        log.TenantId.Should().Be(TenantId);
        log.UserId.Should().Be(UserId);
        log.Action.Should().Be("update");
        log.ResourceType.Should().Be("Lead");
        log.ResourceId.Should().Be(ResourceId);
        log.DiffJson.Should().Be("{\"old\":\"a\",\"new\":\"b\"}");
        log.IpAddress.Should().Be(ip);
        log.UserAgent.Should().Be("Mozilla/5.0");
        log.OccurredAt.Should().Be(Now);
        log.EventKey.Should().BeNull();
        log.StateSequence.Should().BeNull();
    }

    [Fact]
    public void Create_AllowsNullOptionals()
    {
        var log = AuditLog.Create(TenantId, null, "view", "Dashboard", null, Now);

        log.UserId.Should().BeNull();
        log.ResourceId.Should().BeNull();
        log.DiffJson.Should().BeNull();
        log.IpAddress.Should().BeNull();
        log.UserAgent.Should().BeNull();
    }

    // ── CreateBusinessEvent ───────────────────────────────────────────

    [Fact]
    public void CreateBusinessEvent_SetsEventKeyAndSequence()
    {
        var log = AuditLog.CreateBusinessEvent(TenantId, UserId, "state_change", "OrchestrationSession",
            ResourceId, Now, "session.completed", 42, diffJson: "{}");

        log.EventKey.Should().Be("session.completed");
        log.StateSequence.Should().Be(42);
        log.Action.Should().Be("state_change");
        log.DiffJson.Should().Be("{}");
    }

    [Fact]
    public void CreateBusinessEvent_TrimsEventKey()
    {
        var log = AuditLog.CreateBusinessEvent(TenantId, UserId, "act", "Res", null, Now,
            "  spaced.key  ", 1);

        log.EventKey.Should().Be("spaced.key");
    }

    [Fact]
    public void CreateBusinessEvent_ThrowsOnEmptyEventKey()
    {
        var act = () => AuditLog.CreateBusinessEvent(TenantId, UserId, "act", "Res", null, Now, "", 1);

        act.Should().Throw<ArgumentException>().WithParameterName("eventKey");
    }

    [Fact]
    public void CreateBusinessEvent_ThrowsOnWhitespaceEventKey()
    {
        var act = () => AuditLog.CreateBusinessEvent(TenantId, UserId, "act", "Res", null, Now, "   ", 1);

        act.Should().Throw<ArgumentException>().WithParameterName("eventKey");
    }

    [Fact]
    public void CreateBusinessEvent_ThrowsOnEventKeyTooLong()
    {
        var longKey = new string('a', 257);

        var act = () => AuditLog.CreateBusinessEvent(TenantId, UserId, "act", "Res", null, Now, longKey, 1);

        act.Should().Throw<ArgumentException>().WithParameterName("eventKey");
    }

    [Fact]
    public void CreateBusinessEvent_AcceptsMaxLenghtEventKey()
    {
        var maxKey = new string('a', 256);

        var log = AuditLog.CreateBusinessEvent(TenantId, UserId, "act", "Res", null, Now, maxKey, 1);

        log.EventKey.Should().HaveLength(256);
    }

    [Fact]
    public void CreateBusinessEvent_ThrowsOnZeroStateSequence()
    {
        var act = () => AuditLog.CreateBusinessEvent(TenantId, UserId, "act", "Res", null, Now, "key", 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CreateBusinessEvent_ThrowsOnNegativeStateSequence()
    {
        var act = () => AuditLog.CreateBusinessEvent(TenantId, UserId, "act", "Res", null, Now, "key", -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
