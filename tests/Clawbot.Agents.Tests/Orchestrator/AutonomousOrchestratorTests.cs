using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AutonomousOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly AgentDefinitionCatalogEntry Research = new(Guid.NewGuid(), "research-agent", "research", "Research", "research", "Research the goal.", "{}", true);
    private static readonly AgentDefinitionCatalogEntry Content = new(Guid.NewGuid(), "content-agent", "content", "Content", "content", "Write content.", "{}", true);

    [Fact]
    public async Task RunAsync_Completes_WhenAllTasksSucceed()
    {
        var planner = FakePlanner(TwoStepPlan());
        var registry = Registry(
            OkAgent("research-agent", "insight"),
            OkAgent("content-agent", "draft"));
        var sut = Build(planner, registry);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        await planner.DidNotReceive().ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StopsAtMaxRounds_WhenTaskAlwaysFails()
    {
        var planner = FakePlanner(SingleFailingPlan(), SingleFailingPlan());
        var registry = Registry(FailingAgent("failing-agent"));
        var sut = Build(planner, registry, new AutonomousOrchestratorOptions { MaxRounds = 2 });

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("failed");
        result.Reason.Should().Be("max_rounds");
        result.RoundCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_FailsPreflight_WhenCostCapExceeded()
    {
        var planner = FakePlanner(SingleTaskPlan("content-agent"));
        var registry = Registry(OkAgent("content-agent", "ok"));
        var tracker = DeniedTracker();
        var sut = Build(planner, registry, tracker: tracker);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("failed");
        result.Reason.Should().Be("cost_cap_preflight");
    }

    [Fact]
    public async Task RunAsync_PropagatesCancellation()
    {
        var planner = FakePlanner(SingleTaskPlan("content-agent"));
        var registry = Registry(OkAgent("content-agent", "ok"));
        var sut = Build(planner, registry);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.RunAsync(Request("manual"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static AutonomousOrchestrator Build(
        IAutonomousPlanner planner,
        AgentRegistry registry,
        AutonomousOrchestratorOptions? options = null,
        IClaudeCostTracker? tracker = null)
    {
        tracker ??= AllowedTracker();
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Research, Content });
        var mailbox = Substitute.For<IA2AMailbox>();
        var sink = Substitute.For<IAutonomousRunSink>();
        return new AutonomousOrchestrator(
            planner, catalog, registry, mailbox,
            new OrchestratorCostGuard(tracker),
            new LlmCallScope(), sink, new FixedClock(Now), options);
    }

    private static AutonomousRunRequest Request(string source) => new(Tenant, Session, "Launch HSK4 campaign", source, RequiresApproval: false);

    private static IAutonomousPlanner FakePlanner(OrchestrationPlanDocument initial, OrchestrationPlanDocument? replan = null)
    {
        var p = Substitute.For<IAutonomousPlanner>();
        p.PlanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<CancellationToken>()).Returns(initial);
        if (replan is not null)
            p.ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>()).Returns(replan);
        return p;
    }

    private static OrchestrationPlanDocument TwoStepPlan() => new(3, new[]
    {
        new OrchestrationPlanTask("t1", "research-agent", "Research", new Dictionary<string, string>(), Array.Empty<string>(), "pending", null, null),
        new OrchestrationPlanTask("t2", "content-agent", "Write", new Dictionary<string, string>(), new[] { "t1" }, "pending", null, null),
    });

    private static OrchestrationPlanDocument SingleTaskPlan(string agent) => new(3, new[]
    {
        new OrchestrationPlanTask("t1", agent, "Do", new Dictionary<string, string>(), Array.Empty<string>(), "pending", null, null),
    });

    private static OrchestrationPlanDocument SingleFailingPlan() => new(3, new[]
    {
        new OrchestrationPlanTask("t1", "failing-agent", "Do", new Dictionary<string, string>(), Array.Empty<string>(), "pending", null, null),
    });

    private static AgentRegistry Registry(params IAgent[] agents) => new(agents);

    private static IAgent OkAgent(string name, string output)
    {
        var agent = Substitute.For<IAgent>();
        agent.Name.Returns(name);
        agent.ExecuteAsync(Arg.Any<AgentTask>(), Arg.Any<CancellationToken>())
            .Returns(new AgentResult("t1", true, output, null));
        return agent;
    }

    private static IAgent FailingAgent(string name)
    {
        var agent = Substitute.For<IAgent>();
        agent.Name.Returns(name);
        agent.ExecuteAsync(Arg.Any<AgentTask>(), Arg.Any<CancellationToken>())
            .Returns(new AgentResult("t1", false, string.Empty, "boom"));
        return agent;
    }

    private static IClaudeCostTracker AllowedTracker()
    {
        var t = Substitute.For<IClaudeCostTracker>();
        t.SummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CostSummary(Tenant, 0m, 1000m, 0));
        return t;
    }

    private static IClaudeCostTracker DeniedTracker()
    {
        var t = Substitute.For<IClaudeCostTracker>();
        t.SummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CostSummary(Tenant, 1000m, 1m, 100));
        return t;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
