using Clawbot.Domain.Experiments;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Experiments;

public sealed class ExperimentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var experiment = Experiment.Create(TenantId, "exp-01", "ChatScenario", TargetId, "Test Prompt A vs B", Now);

        experiment.TenantId.Should().Be(TenantId);
        experiment.Code.Should().Be("exp-01");
        experiment.TargetType.Should().Be("chatscenario");
        experiment.TargetId.Should().Be(TargetId);
        experiment.Name.Should().Be("Test Prompt A vs B");
        experiment.Status.Should().Be("active");
        experiment.CreatedAt.Should().Be(Now);
        experiment.UpdatedAt.Should().BeNull();
        experiment.DeletedAt.Should().BeNull();
        experiment.Variants.Should().BeEmpty();
    }

    [Fact]
    public void Create_NormalizesTargetTypeToLower()
    {
        var experiment = Experiment.Create(TenantId, "e", "CHATSCENARIO", TargetId, "n", Now);

        experiment.TargetType.Should().Be("chatscenario");
    }

    [Fact]
    public void Create_TrimsStrings()
    {
        var experiment = Experiment.Create(TenantId, "  exp-02  ", "  KbVersion  ", TargetId, "  Name  ", Now);

        experiment.Code.Should().Be("exp-02");
        experiment.TargetType.Should().Be("kbversion");
        experiment.Name.Should().Be("Name");
    }

    [Fact]
    public void AddVariant_CreatesAndAppendsVariant()
    {
        var experiment = Experiment.Create(TenantId, "e", "chatscenario", TargetId, "n", Now);
        var scenarioId = Guid.NewGuid();

        var variant = experiment.AddVariant("A", "Control", 50, scenarioId, null, Now.AddMinutes(1));

        variant.Should().NotBeNull();
        variant.Code.Should().Be("A");
        variant.Name.Should().Be("Control");
        variant.Weight.Should().Be(50);
        variant.ChatScenarioId.Should().Be(scenarioId);
        variant.KbVersionId.Should().BeNull();
        experiment.Variants.Should().HaveCount(1);
        experiment.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void AddVariant_ThrowsOnZeroWeight()
    {
        var experiment = Experiment.Create(TenantId, "e", "chatscenario", TargetId, "n", Now);

        var act = () => experiment.AddVariant("A", "Control", 0, null, null, Now);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("weight");
    }

    [Fact]
    public void AddVariant_ThrowsOnNegativeWeight()
    {
        var experiment = Experiment.Create(TenantId, "e", "chatscenario", TargetId, "n", Now);

        var act = () => experiment.AddVariant("A", "Control", -1, null, null, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddVariant_MultipleVariantsAccumulate()
    {
        var experiment = Experiment.Create(TenantId, "e", "chatscenario", TargetId, "n", Now);

        experiment.AddVariant("A", "Control", 50, null, null, Now.AddMinutes(1));
        experiment.AddVariant("B", "Treatment", 50, null, null, Now.AddMinutes(2));

        experiment.Variants.Should().HaveCount(2);
    }

    [Fact]
    public void Stop_SetsStatusStopped()
    {
        var experiment = Experiment.Create(TenantId, "e", "chatscenario", TargetId, "n", Now);

        experiment.Stop(Now.AddHours(1));

        experiment.Status.Should().Be("stopped");
        experiment.UpdatedAt.Should().Be(Now.AddHours(1));
    }
}
