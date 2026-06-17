using Clawbot.AgentService.Services;
using Clawbot.Agents.Core;
using FluentAssertions;

namespace Clawbot.AgentService.Tests.Services;

public sealed class DefaultAgentRegistryTests
{
    [Fact]
    public async Task Default_registry_exposes_all_runtime_agent_service_names()
    {
        var registry = DefaultAgentRegistry.Create();

        registry.Names.Should().BeEquivalentTo(
            "ads",
            "chat",
            "content",
            "docs",
            "lead",
            "report",
            "research",
            "sale_assist");

        var task = new AgentTask(
            "task-001",
            "chat",
            "Route a chat task",
            new Dictionary<string, string> { ["goal"] = "answer learner question" });
        var result = await registry.Resolve("chat").ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("ChatAgentGrpcService");
    }
}
