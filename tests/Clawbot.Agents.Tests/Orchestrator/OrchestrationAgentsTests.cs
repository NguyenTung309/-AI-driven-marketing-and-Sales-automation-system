using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class OrchestrationAgentsTests
{
    [Fact]
    public void Build_maps_code_shortname_and_agenttype_to_same_adapter()
    {
        var content = new StubAgent("content-agent");
        var catalog = new[]
        {
            new AgentCatalogEntry("content-agent", "content", "Content", "content", "d", "{}", Orchestratable: true),
        };

        var map = OrchestrationAgents.Build([content], catalog);

        map["content-agent"].Should().BeSameAs(content);
        map["content"].Should().BeSameAs(content);
        map["CONTENT"].Should().BeSameAs(content);
    }

    [Fact]
    public void Build_ignores_catalog_entries_without_a_registered_adapter()
    {
        var content = new StubAgent("content-agent");
        var catalog = new[]
        {
            new AgentCatalogEntry("ghost-agent", "ghost", "Ghost", "ghost", "d", "{}", Orchestratable: true),
        };

        var map = OrchestrationAgents.Build([content], catalog);

        map.Should().ContainKey("content-agent");
        map.Should().NotContainKey("ghost");
    }

    private sealed class StubAgent(string name) : IAgent
    {
        public string Name { get; } = name;
        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct) =>
            Task.FromResult(new AgentResult(task.Id, Success: true, Output: "ok", Error: null));
    }
}
