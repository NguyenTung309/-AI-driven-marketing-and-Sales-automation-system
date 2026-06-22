using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class PlanningOrchestratorTests
{
    [Fact]
    public void Plan_creates_tasks_for_registered_agents_mentioned_in_goal_in_goal_order()
    {
        var orchestrator = new PlanningOrchestrator(new AgentRegistry([
            Agent("lead"),
            Agent("chat"),
            Agent("content"),
        ]));

        var plan = orchestrator.Plan("tenant-1", "Ask chat to qualify the learner, then hand off to lead.");

        plan.Tasks.Select(task => task.AgentName).Should().Equal("chat", "lead");
        plan.Tasks.Select(task => task.Id).Should().Equal("task-001", "task-002");
        plan.Tasks[0].Input["tenant_id"].Should().Be("tenant-1");
        plan.Tasks[0].Input["goal"].Should().Contain("qualify the learner");
    }

    [Fact]
    public void Plan_returns_empty_plan_when_goal_has_no_agent_name()
    {
        var orchestrator = new PlanningOrchestrator(new AgentRegistry([
            Agent("lead"),
            Agent("chat"),
        ]));

        var plan = orchestrator.Plan("tenant-1", "Build the daily operations plan.");

        plan.Tasks.Should().BeEmpty();
    }

    [Fact]
    public void Plan_matches_agent_names_as_whole_references_only()
    {
        var orchestrator = new PlanningOrchestrator(new AgentRegistry([
            Agent("lead"),
            Agent("ads"),
        ]));

        var plan = orchestrator.Plan("tenant-1", "Work on leads pipeline.");

        plan.Tasks.Select(task => task.AgentName).Should().Equal("lead");
    }

    [Fact]
    public void Plan_ignores_agent_name_substrings_inside_words()
    {
        var orchestrator = new PlanningOrchestrator(new AgentRegistry([
            Agent("lead"),
        ]));

        var plan = orchestrator.Plan("tenant-1", "Improve leadership training.");

        plan.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_records_trace_events_for_each_task()
    {
        var chat = Agent("chat");
        chat.ExecuteAsync(Arg.Any<AgentTask>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var task = call.Arg<AgentTask>();
                return Task.FromResult(new AgentResult(task.Id, Success: true, Output: "done", Error: null));
            });
        var orchestrator = new PlanningOrchestrator(new AgentRegistry([chat]));
        var plan = orchestrator.Plan("tenant-1", "chat with the learner");

        var results = new List<AgentResult>();
        await foreach (var result in orchestrator.ExecuteAsync(plan))
        {
            results.Add(result);
        }

        results.Should().ContainSingle(result => result.Success);
        orchestrator.GetTrace(plan.SessionId, "tenant-1")
            .Select(trace => trace.Phase)
            .Should().ContainInOrder("planned", "started", "completed");
    }

    private static IAgent Agent(string name)
    {
        var agent = Substitute.For<IAgent>();
        agent.Name.Returns(name);
        return agent;
    }
}
