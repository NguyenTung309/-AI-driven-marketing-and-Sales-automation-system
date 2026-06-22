using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class ParallelDagExecutorReplanTests
{
    [Fact]
    public async Task ExecuteAsync_replans_once_then_succeeds()
    {
        var attempts = 0;
        var agents = new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["flaky"] = new FailingAgent("flaky"),
            ["stable"] = new OkAgent("stable"),
        };

        ParallelDagExecutor.ReplanCallback replanner = (current, failed, attempt, ct) =>
        {
            attempts = attempt;
            // Patch the failed task to use the stable agent, reset to pending.
            var patched = current with
            {
                Tasks = current.Tasks
                    .Select(task => failed.Any(f => f.Id == task.Id)
                        ? task with { Agent = "stable", Status = "pending", Output = null, Error = null }
                        : task)
                    .ToArray(),
            };
            return Task.FromResult<OrchestrationPlanDocument?>(patched);
        };

        var executor = new ParallelDagExecutor(agents, maxConcurrency: 3, replanner, maxReplans: 2);
        var plan = new OrchestrationPlanDocument(1, [Node("t1", "flaky")]);

        var result = await executor.ExecuteAsync(plan, CancellationToken.None);

        attempts.Should().Be(1);
        result.Tasks.Single().Status.Should().Be("completed");
    }

    [Fact]
    public async Task ExecuteAsync_does_not_replan_after_cost_cap_stop()
    {
        var calls = 0;
        var agents = new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["costly"] = new FailingAgent("costly", "cost_cap_midrun"),
        };
        ParallelDagExecutor.ReplanCallback replanner = (current, failed, attempt, ct) =>
        {
            calls++;
            return Task.FromResult<OrchestrationPlanDocument?>(current);
        };

        var executor = new ParallelDagExecutor(agents, maxConcurrency: 3, replanner, maxReplans: 2);
        var result = await executor.ExecuteAsync(new OrchestrationPlanDocument(1, [Node("t1", "costly")]), CancellationToken.None);

        calls.Should().Be(0);
        result.Tasks.Single().Status.Should().Be("failed");
        result.Tasks.Single().Error.Should().Be("cost_cap_midrun");
    }

    [Fact]
    public async Task ExecuteAsync_stops_after_max_replans()
    {
        var calls = 0;
        var agents = new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["flaky"] = new FailingAgent("flaky"),
        };

        ParallelDagExecutor.ReplanCallback replanner = (current, failed, attempt, ct) =>
        {
            calls++;
            // Keep handing back the same failing task as pending again.
            var patched = current with
            {
                Tasks = current.Tasks
                    .Select(task => task with { Status = "pending", Output = null, Error = null })
                    .ToArray(),
            };
            return Task.FromResult<OrchestrationPlanDocument?>(patched);
        };

        var executor = new ParallelDagExecutor(agents, maxConcurrency: 3, replanner, maxReplans: 2);
        var plan = new OrchestrationPlanDocument(1, [Node("t1", "flaky")]);

        var result = await executor.ExecuteAsync(plan, CancellationToken.None);

        calls.Should().Be(2);
        result.Tasks.Single().Status.Should().Be("failed");
    }

    private static OrchestrationPlanTask Node(string id, string agent, params string[] dependsOn) =>
        new(id, agent, id, new Dictionary<string, string>(), dependsOn, "pending", null, null);

    private sealed class OkAgent(string name) : IAgent
    {
        public string Name { get; } = name;
        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct) =>
            Task.FromResult(new AgentResult(task.Id, Success: true, Output: "ok", Error: null));
    }

    private sealed class FailingAgent(string name, string error = "boom") : IAgent
    {
        public string Name { get; } = name;
        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct) =>
            Task.FromResult(new AgentResult(task.Id, Success: false, Output: string.Empty, Error: error));
    }
}
