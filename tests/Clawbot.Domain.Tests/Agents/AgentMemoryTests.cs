using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentMemoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var memory = AgentMemory.Create(TenantId, "reviewer-agent", "Thiếu CTA ở cuối bài", "mistake", 0.8m, Now);

        memory.TenantId.Should().Be(TenantId);
        memory.AgentCode.Should().Be("reviewer-agent");
        memory.Fact.Should().Be("Thiếu CTA ở cuối bài");
        memory.Category.Should().Be("mistake");
        memory.Confidence.Should().Be(0.8m);
        memory.IsActive.Should().BeTrue();
        memory.SupersededById.Should().BeNull();
        memory.CreatedAt.Should().Be(Now);
        memory.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_ThrowsOnBlankAgentCode()
    {
        var act = () => AgentMemory.Create(TenantId, "  ", "fact", "cat", 0.5m, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("agentCode");
    }

    [Fact]
    public void Create_ThrowsOnBlankFact()
    {
        var act = () => AgentMemory.Create(TenantId, "agent", "", "cat", 0.5m, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("fact");
    }

    [Fact]
    public void Create_ThrowsOnBlankCategory()
    {
        var act = () => AgentMemory.Create(TenantId, "agent", "fact", "  ", 0.5m, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("category");
    }

    [Fact]
    public void Create_ClampsConfidenceToZeroOne()
    {
        var low = AgentMemory.Create(TenantId, "a", "f", "c", -0.5m, Now);
        var high = AgentMemory.Create(TenantId, "a", "f", "c", 1.5m, Now);

        low.Confidence.Should().Be(0m);
        high.Confidence.Should().Be(1m);
    }

    [Fact]
    public void Create_TrimsStrings()
    {
        var memory = AgentMemory.Create(TenantId, "  agent  ", "  fact  ", "  cat  ", 0.5m, Now);

        memory.AgentCode.Should().Be("agent");
        memory.Fact.Should().Be("fact");
        memory.Category.Should().Be("cat");
    }

    [Fact]
    public void Supersede_DeactivatesAndPointsToReplacement()
    {
        var memory = AgentMemory.Create(TenantId, "a", "old lesson", "mistake", 0.9m, Now);
        var replacementId = Guid.NewGuid();

        memory.Supersede(replacementId, Now.AddMinutes(5));

        memory.IsActive.Should().BeFalse();
        memory.SupersededById.Should().Be(replacementId);
        memory.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Supersede_AllowsNullReplacementForDelete()
    {
        var memory = AgentMemory.Create(TenantId, "a", "f", "c", 0.5m, Now);

        memory.Supersede(null, Now.AddMinutes(1));

        memory.IsActive.Should().BeFalse();
        memory.SupersededById.Should().BeNull();
    }

    [Fact]
    public void Supersede_ThrowsWhenAlreadySuperseded()
    {
        var memory = AgentMemory.Create(TenantId, "a", "f", "c", 0.5m, Now);
        memory.Supersede(Guid.NewGuid(), Now);

        var act = () => memory.Supersede(Guid.NewGuid(), Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }
}
