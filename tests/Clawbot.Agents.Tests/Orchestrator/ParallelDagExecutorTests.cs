using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class ParallelDagExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_runs_dependencies_before_dependents()
    {
        var order = new List<string>();
        var executor = new ParallelDagExecutor(new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["research"] = new RecordingAgent("research", order),
            ["content"] = new RecordingAgent("content", order),
        }, maxConcurrency: 3);
        var plan = new OrchestrationPlanDocument(1,
        [
            Task("t1", "research"),
            Task("t2", "content", "t1"),
        ]);

        var result = await executor.ExecuteAsync(plan, CancellationToken.None);

        order.Should().Equal("t1", "t2");
        result.Tasks.Select(task => task.Status).Should().Equal("completed", "completed");
    }

    [Fact]
    public async Task ExecuteAsync_runs_independent_tasks_with_concurrency_limit()
    {
        var active = 0;
        var peak = 0;
        var executor = new ParallelDagExecutor(new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new DelayedAgent("a", () =>
            {
                var now = Interlocked.Increment(ref active);
                peak = Math.Max(peak, now);
            }, () => Interlocked.Decrement(ref active)),
            ["b"] = new DelayedAgent("b", () =>
            {
                var now = Interlocked.Increment(ref active);
                peak = Math.Max(peak, now);
            }, () => Interlocked.Decrement(ref active)),
            ["c"] = new DelayedAgent("c", () =>
            {
                var now = Interlocked.Increment(ref active);
                peak = Math.Max(peak, now);
            }, () => Interlocked.Decrement(ref active)),
        }, maxConcurrency: 2);
        var plan = new OrchestrationPlanDocument(1, [Task("t1", "a"), Task("t2", "b"), Task("t3", "c")]);

        await executor.ExecuteAsync(plan, CancellationToken.None);

        peak.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_marks_failed_task_and_skips_dependents()
    {
        var executor = new ParallelDagExecutor(new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["research"] = new FailingAgent("research"),
            ["content"] = new RecordingAgent("content", []),
        }, maxConcurrency: 3);
        var plan = new OrchestrationPlanDocument(1, [Task("t1", "research"), Task("t2", "content", "t1")]);

        var result = await executor.ExecuteAsync(plan, CancellationToken.None);

        result.Tasks[0].Status.Should().Be("failed");
        result.Tasks[1].Status.Should().Be("skipped");
    }

    [Fact]
    public async Task SerializingAgent_runs_wrapped_tasks_one_at_a_time()
    {
        var active = 0;
        var peak = 0;
        using var gate = new SemaphoreSlim(1, 1);
        var executor = new ParallelDagExecutor(new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["lead"] = new SerializingAgent(new DelayedAgent("lead", () =>
            {
                var now = Interlocked.Increment(ref active);
                peak = Math.Max(peak, now);
            }, () => Interlocked.Decrement(ref active)), gate),
        }, maxConcurrency: 3);
        var plan = new OrchestrationPlanDocument(1, [Task("t1", "lead"), Task("t2", "lead"), Task("t3", "lead")]);

        await executor.ExecuteAsync(plan, CancellationToken.None);

        peak.Should().Be(1);
    }

    private static OrchestrationPlanTask Task(string id, string agent, params string[] dependsOn) =>
        new(id, agent, id, new Dictionary<string, string>(), dependsOn, "pending", null, null);

    private sealed class RecordingAgent(string name, List<string> order) : IAgent
    {
        public string Name { get; } = name;

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
        {
            order.Add(task.Id);
            return System.Threading.Tasks.Task.FromResult(new AgentResult(task.Id, Success: true, Output: "ok", Error: null));
        }
    }

    private sealed class DelayedAgent(string name, Action onStart, Action onStop) : IAgent
    {
        public string Name { get; } = name;

        public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
        {
            onStart();
            await System.Threading.Tasks.Task.Delay(50, ct);
            onStop();
            return new AgentResult(task.Id, Success: true, Output: "ok", Error: null);
        }
    }

    private sealed class FailingAgent(string name) : IAgent
    {
        public string Name { get; } = name;
        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(new AgentResult(task.Id, Success: false, Output: string.Empty, Error: "boom"));
    }
}
