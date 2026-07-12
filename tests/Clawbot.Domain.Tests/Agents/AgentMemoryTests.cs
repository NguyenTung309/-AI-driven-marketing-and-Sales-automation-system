using Clawbot.Domain.Agents;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentMemoryTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 12, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_and_clamps()
    {
        var memory = AgentMemory.Create(TenantId, " reviewer-agent ", "  Content hay bịa giá khóa học  ", "mistake", 1.5m, CreatedAt);

        memory.AgentCode.Should().Be("reviewer-agent");
        memory.Fact.Should().Be("Content hay bịa giá khóa học");
        memory.Confidence.Should().Be(1m);
        memory.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(" ", "f", "mistake", "agent_code_required")]
    [InlineData("reviewer-agent", " ", "mistake", "fact_required")]
    [InlineData("reviewer-agent", "f", " ", "category_required")]
    public void Create_rejects_missing_fields(string agentCode, string fact, string category, string error)
    {
        var act = () => AgentMemory.Create(TenantId, agentCode, fact, category, 0.9m, CreatedAt);

        act.Should().Throw<ArgumentException>().WithMessage($"{error}*");
    }

    [Fact]
    public void Supersede_is_one_way()
    {
        var memory = AgentMemory.Create(TenantId, "reviewer-agent", "Lỗi X", "mistake", 0.9m, CreatedAt);

        memory.Supersede(null, CreatedAt.AddDays(1));

        memory.IsActive.Should().BeFalse();
        var again = () => memory.Supersede(null, CreatedAt.AddDays(2));
        again.Should().Throw<InvalidOperationException>().WithMessage("memory_already_superseded");
    }
}
