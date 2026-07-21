using Clawbot.Domain.Security;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Security;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_without_business_identity_remains_backward_compatible()
    {
        var audit = AuditLog.Create(
            Guid.NewGuid(),
            userId: null,
            action: "content.read",
            resourceType: "content_item",
            resourceId: Guid.NewGuid(),
            occurredAt: Now);

        audit.EventKey.Should().BeNull();
        audit.StateSequence.Should().BeNull();
    }

    [Fact]
    public void CreateBusinessEvent_records_deterministic_identity()
    {
        var tenantId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        var audit = AuditLog.CreateBusinessEvent(
            tenantId,
            userId: null,
            action: "content.agent_review.completed",
            resourceType: "content_item",
            resourceId,
            occurredAt: Now,
            eventKey: $"content:item:{resourceId:N}:revision:2:review-completed",
            stateSequence: 3,
            diffJson: "{\"status\":\"passed\"}");

        audit.EventKey.Should().Be($"content:item:{resourceId:N}:revision:2:review-completed");
        audit.StateSequence.Should().Be(3);
        audit.DiffJson.Should().Be("{\"status\":\"passed\"}");
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("event", 0)]
    [InlineData("event", -1)]
    public void CreateBusinessEvent_rejects_invalid_identity(string eventKey, long sequence)
    {
        var act = () => AuditLog.CreateBusinessEvent(
            Guid.NewGuid(),
            userId: null,
            action: "content.agent_review.completed",
            resourceType: "content_item",
            resourceId: Guid.NewGuid(),
            occurredAt: Now,
            eventKey,
            stateSequence: sequence);

        act.Should().Throw<ArgumentException>();
    }
}
