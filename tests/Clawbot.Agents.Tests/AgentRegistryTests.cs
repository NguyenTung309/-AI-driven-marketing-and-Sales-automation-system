using Clawbot.Agents.Core;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests;

// Core — AgentRegistry case-insensitive resolution.
public sealed class AgentRegistryTests
{
    private static IAgent Agent(string name)
    {
        var agent = Substitute.For<IAgent>();
        agent.Name.Returns(name);
        return agent;
    }

    [Fact]
    public void Resolves_case_insensitively()
    {
        var registry = new AgentRegistry(new[] { Agent("chat") });

        registry.Resolve("CHAT").Name.Should().Be("chat");
    }

    [Fact]
    public void Unknown_agent_throws()
    {
        var registry = new AgentRegistry(new[] { Agent("chat") });

        var act = () => registry.Resolve("nope");

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Names_lists_all_registered()
    {
        var registry = new AgentRegistry(new[] { Agent("chat"), Agent("lead") });

        registry.Names.Should().BeEquivalentTo("chat", "lead");
    }
}
