using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class OrchestrationPlanValidatorTests
{
    private static readonly AgentCatalogEntry Content = new(
        "content-agent", "content", "Content", "content", "Run content", "{}", Orchestratable: true);

    private static readonly AgentCatalogEntry Research = new(
        "research-agent", "research", "Research", "research", "Run research", "{}", Orchestratable: true);

    [Fact]
    public void Validate_accepts_acyclic_plan_with_known_agents()
    {
        var plan = new OrchestrationPlanDocument(1,
        [
            new OrchestrationPlanTask("t1", "research", "scan", new Dictionary<string, string>(), [], "pending", null, null),
            new OrchestrationPlanTask("t2", "content-agent", "write", new Dictionary<string, string>(), ["t1"], "pending", null, null),
        ]);

        var result = OrchestrationPlanValidator.Validate(plan, [Content, Research]);

        result.IsValid.Should().BeTrue(result.Error);
    }

    [Fact]
    public void Validate_rejects_unknown_agents()
    {
        var plan = new OrchestrationPlanDocument(1,
        [
            new OrchestrationPlanTask("t1", "missing", "scan", new Dictionary<string, string>(), [], "pending", null, null),
        ]);

        var result = OrchestrationPlanValidator.Validate(plan, [Content]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().StartWith("unknown_agent:t1:missing");
    }

    [Fact]
    public void Validate_rejects_dangling_dependencies()
    {
        var plan = new OrchestrationPlanDocument(1,
        [
            new OrchestrationPlanTask("t1", "content", "write", new Dictionary<string, string>(), ["missing"], "pending", null, null),
        ]);

        var result = OrchestrationPlanValidator.Validate(plan, [Content]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("dangling_dependency:t1:missing");
    }

    [Fact]
    public void Validate_rejects_cycles()
    {
        var plan = new OrchestrationPlanDocument(1,
        [
            new OrchestrationPlanTask("t1", "research", "scan", new Dictionary<string, string>(), ["t2"], "pending", null, null),
            new OrchestrationPlanTask("t2", "content", "write", new Dictionary<string, string>(), ["t1"], "pending", null, null),
        ]);

        var result = OrchestrationPlanValidator.Validate(plan, [Content, Research]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("cycle_detected");
    }

    [Fact]
    public void Validate_rejects_too_many_tasks()
    {
        var tasks = Enumerable.Range(1, OrchestrationPlanValidator.MaxTaskCount + 1)
            .Select(index => new OrchestrationPlanTask($"t{index}", "content", "write", new Dictionary<string, string>(), [], "pending", null, null))
            .ToArray();
        var plan = new OrchestrationPlanDocument(1, tasks);

        var result = OrchestrationPlanValidator.Validate(plan, [Content]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be($"too_many_tasks:{OrchestrationPlanValidator.MaxTaskCount + 1}:{OrchestrationPlanValidator.MaxTaskCount}");
    }

    [Fact]
    public void Validate_rejects_oversized_task_input()
    {
        var input = new Dictionary<string, string> { ["brief"] = new('x', OrchestrationPlanValidator.MaxTaskInputChars + 1) };
        var plan = new OrchestrationPlanDocument(1,
        [
            new OrchestrationPlanTask("t1", "content", "write", input, [], "pending", null, null),
        ]);

        var result = OrchestrationPlanValidator.Validate(plan, [Content]);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("input_too_large:t1");
    }

    [Fact]
    public void WithTaskStatus_updates_one_task_immutably()
    {
        var task = new OrchestrationPlanTask("t1", "content", "write", new Dictionary<string, string>(), [], "pending", null, null);
        var plan = new OrchestrationPlanDocument(1, [task]);

        var updated = plan.WithTaskStatus("t1", "completed", "ok", null);

        updated.Should().NotBeSameAs(plan);
        updated.Tasks.Should().ContainSingle().Which.Status.Should().Be("completed");
        plan.Tasks.Should().ContainSingle().Which.Status.Should().Be("pending");
    }
}
