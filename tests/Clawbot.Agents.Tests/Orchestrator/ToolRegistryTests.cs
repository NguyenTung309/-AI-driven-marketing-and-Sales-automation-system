using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class ToolRegistryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Build_WrapsOnlyAdaptersWithDeclaredMetadata()
    {
        // EARS[WHEN the tool registry is built from adapters THE SYSTEM SHALL expose only those with declared
        // metadata, skipping unknown adapters rather than guessing a permission]
        var withMeta = new FakeAgent("content-agent");
        var withoutMeta = new FakeAgent("mystery-agent");

        var registry = ToolRegistryFactory.Build(new[] { withMeta, withoutMeta });

        registry.Resolve("content-agent").Should().NotBeNull();
        registry.Resolve("mystery-agent").Should().BeNull();
        registry.All.Should().ContainSingle(t => t.Name == "content-agent");
    }

    [Fact]
    public void Resolve_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        var registry = ToolRegistryFactory.Build(new[] { new FakeAgent("content-agent") });

        registry.Resolve("CONTENT-AGENT").Should().NotBeNull();
        registry.Resolve("missing").Should().BeNull();
    }

    [Fact]
    public void AllowedFor_FiltersToAllowList_AndDropsUnknownNames()
    {
        // EARS[WHEN an agent declares an allowed-tools list THE SYSTEM SHALL expose only the resolvable tools in it,
        // dropping unknown names so a stale allow-list never broadens capability]
        var registry = ToolRegistryFactory.Build(new[]
        {
            new FakeAgent("content-agent"),
            new FakeAgent("ads-agent"),
        });

        var allowed = registry.AllowedFor(new[] { "content-agent", "ghost-tool", "ADS-AGENT" });

        allowed.Should().HaveCount(2);
        allowed.Select(t => t.Name).Should().BeEquivalentTo(new[] { "content-agent", "ads-agent" });
    }

    [Fact]
    public void AllowedFor_EmptyList_ReturnsEmpty()
    {
        var registry = ToolRegistryFactory.Build(new[] { new FakeAgent("content-agent") });

        registry.AllowedFor(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public async Task Invoke_ForwardsTenantIdAndArgs_AndMapsSuccessToToolResult()
    {
        // EARS[WHEN a tool is invoked THE SYSTEM SHALL forward the tool args plus tenant_id (from context) into the
        // underlying adapter and map a successful AgentResult to a successful ToolResult]
        var adapter = new FakeAgent("content-agent");
        var registry = ToolRegistryFactory.Build(new[] { adapter });
        var tool = registry.Resolve("content-agent")!;

        var result = await tool.InvokeAsync(
            new Dictionary<string, string> { ["platform"] = "facebook", ["brief"] = "hi" },
            new ToolContext(Tenant, "task-1"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        adapter.LastInput.Should().ContainKey("tenant_id").WhoseValue.Should().Be(Tenant.ToString("D"));
        adapter.LastInput.Should().ContainKey("platform").WhoseValue.Should().Be("facebook");
    }

    [Fact]
    public async Task Invoke_MapsAdapterFailureToFailedToolResult()
    {
        var adapter = new FakeAgent("content-agent", succeeds: false, error: "missing brief");
        var registry = ToolRegistryFactory.Build(new[] { adapter });
        var tool = registry.Resolve("content-agent")!;

        var result = await tool.InvokeAsync(
            new Dictionary<string, string>(),
            new ToolContext(Tenant, "task-1"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("missing brief");
    }

    [Fact]
    public void Build_ExplicitToolOverridesAdapterWrappedOfSameName()
    {
        // EARS[WHEN an explicit IAgentTool shares a name with an adapter-wrapped tool THE SYSTEM SHALL use the
        // explicit one (AgentService-layer tools with AppDbContext win over text-only Core adapters)]
        var adapter = new FakeAgent("content-agent");
        var explicitTool = new ExplicitTool("content-agent", "persisting content tool");

        var registry = ToolRegistryFactory.Build(new[] { adapter }, new[] { explicitTool });

        var resolved = registry.Resolve("content-agent");
        resolved.Should().NotBeNull();
        resolved.Should().BeSameAs(explicitTool);
        resolved!.Description.Should().Be("persisting content tool");
    }

    [Fact]
    public void Tools_CarryRequiredPermissionMetadata()
    {
        var registry = ToolRegistryFactory.Build(new[] { new FakeAgent("content-agent"), new FakeAgent("report-agent") });

        registry.Resolve("content-agent")!.RequiredPermission.Should().Be("content:write");
        registry.Resolve("report-agent")!.RequiredPermission.Should().Be("analytics:read");
    }

    private sealed class FakeAgent : IAgent
    {
        private readonly bool _succeeds;
        private readonly string _error;
        private int _calls;

        public FakeAgent(string name, bool succeeds = true, string error = "")
        {
            Name = name;
            _succeeds = succeeds;
            _error = error;
        }

        public string Name { get; }
        public int Calls => _calls;
        public IReadOnlyDictionary<string, string> LastInput { get; private set; } = new Dictionary<string, string>(0);

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken cancellationToken)
        {
            ++_calls;
            LastInput = task.Input;
            return Task.FromResult(_succeeds
                ? new AgentResult(task.Id, true, "{\"ok\":true}", null)
                : new AgentResult(task.Id, false, string.Empty, _error));
        }
    }

    // Minimal explicit tool to assert the override + ctx-forwarding contract without pulling AppDbContext into Agents.Tests.
    private sealed class ExplicitTool : IAgentTool
    {
        public ExplicitTool(string name, string description) { Name = name; Description = description; }
        public string Name { get; }
        public string Description { get; }
        public string InputSchemaJson => "{}";
        public string RequiredPermission => "content:write";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
        public Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
            => Task.FromResult(ToolResult.Ok("explicit"));
    }
}
