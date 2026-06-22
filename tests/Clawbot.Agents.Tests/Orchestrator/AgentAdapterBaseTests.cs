using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AgentAdapterBaseTests
{
    private static AgentTask MakeTask() =>
        new("t1", "probe", "desc", new Dictionary<string, string>());

    [Fact]
    public async Task ExecuteAsync_returns_success_with_core_output()
    {
        var adapter = new ProbeAdapter(_ => "rendered");

        var result = await adapter.ExecuteAsync(MakeTask(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("rendered");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_maps_core_exception_to_failed_result()
    {
        var adapter = new ProbeAdapter(_ => throw new ArgumentException("tenant_id is required."));

        var result = await adapter.ExecuteAsync(MakeTask(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Output.Should().BeEmpty();
        result.Error.Should().Be("tenant_id is required.");
    }

    private sealed class ProbeAdapter(Func<AgentTask, string> core) : AgentAdapterBase("probe")
    {
        protected override Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct) =>
            Task.FromResult(core(task));
    }
}
