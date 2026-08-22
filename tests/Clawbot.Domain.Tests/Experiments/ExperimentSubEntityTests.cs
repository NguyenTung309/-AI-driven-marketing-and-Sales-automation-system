using Clawbot.Domain.Experiments;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Experiments;

public sealed class ExperimentAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFieldsAndTrimsSubjectKey()
    {
        var expId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        var assignment = ExperimentAssignment.Create(TenantId, expId, variantId, "  user-123  ", Now);

        assignment.TenantId.Should().Be(TenantId);
        assignment.ExperimentId.Should().Be(expId);
        assignment.VariantId.Should().Be(variantId);
        assignment.SubjectKey.Should().Be("user-123");
        assignment.AssignedAt.Should().Be(Now);
    }
}

public sealed class ExperimentEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_NormalizesEventTypeAndTrims()
    {
        var expId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        var evt = ExperimentEvent.Create(TenantId, expId, variantId, "  user-1  ", "  CONVERSION  ", 99.5m, Now);

        evt.EventType.Should().Be("conversion");
        evt.SubjectKey.Should().Be("user-1");
        evt.Value.Should().Be(99.5m);
        evt.OccurredAt.Should().Be(Now);
    }

    [Fact]
    public void Create_NullValue_Allowed()
    {
        var evt = ExperimentEvent.Create(TenantId, Guid.NewGuid(), Guid.NewGuid(), "u", "click", null, Now);

        evt.Value.Should().BeNull();
    }
}
