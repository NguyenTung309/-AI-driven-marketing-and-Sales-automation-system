using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class OrchestrationReplanTests
{
    [Fact]
    public void Merge_preserves_completed_tasks_and_prefixes_fresh_tasks()
    {
        var current = new OrchestrationPlanDocument(1,
        [
            new("t1", "research", "done", Dict(), [], "completed", "result", null),
            new("t2", "content", "x", Dict(), ["t1"], "failed", null, "boom"),
        ]);
        var regenerated = new OrchestrationPlanDocument(1,
        [
            new("a", "content", "retry", Dict(), [], "pending", null, null),
            new("b", "docs", "doc", Dict(), ["a"], "pending", null, null),
        ]);

        var merged = OrchestrationReplan.Merge(current, regenerated, attempt: 1);

        merged.Tasks.Should().HaveCount(3);
        merged.Tasks[0].Id.Should().Be("t1");
        merged.Tasks[0].Status.Should().Be("completed");
        merged.Tasks[1].Id.Should().Be("r1-a");
        merged.Tasks[2].Id.Should().Be("r1-b");
        merged.Tasks[2].DependsOn.Should().Equal("r1-a");
    }

    [Fact]
    public void Merge_keeps_dependencies_to_preserved_completed_tasks_unprefixed()
    {
        var current = new OrchestrationPlanDocument(1,
        [
            new("t1", "research", "done", Dict(), [], "completed", "result", null),
            new("t2", "content", "x", Dict(), ["t1"], "failed", null, "boom"),
        ]);
        var regenerated = new OrchestrationPlanDocument(1,
        [
            new("a", "content", "retry", Dict(), ["t1"], "pending", null, null),
            new("b", "docs", "doc", Dict(), ["a"], "pending", null, null),
        ]);

        var merged = OrchestrationReplan.Merge(current, regenerated, attempt: 1);

        merged.Tasks[1].DependsOn.Should().Equal("t1");
        merged.Tasks[2].DependsOn.Should().Equal("r1-a");
    }

    [Fact]
    public void BuildReplanGoal_includes_failure_context()
    {
        var failed = new[] { new OrchestrationPlanTask("t2", "content", "x", Dict(), [], "failed", null, "rate_limited") };

        var goal = OrchestrationReplan.BuildReplanGoal("launch HSK4", failed);

        goal.Should().Contain("launch HSK4");
        goal.Should().Contain("content:rate_limited");
    }

    private static Dictionary<string, string> Dict() => new();
}
