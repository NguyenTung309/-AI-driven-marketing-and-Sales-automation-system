using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
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
    private static readonly AgentDefinitionCatalogEntry Research = new(Guid.NewGuid(), "research-agent", "research", "Research", "research", "Research the goal.", "{}", true, null);
    private static readonly AgentDefinitionCatalogEntry Content = new(Guid.NewGuid(), "content-agent", "content", "Content", "content", "Write content.", "{}", true, null);

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
    public async Task RunAsync_UsesDynamicDefinitionPersonaAndKbSnippets()
    {
        var definition = new AgentDefinitionCatalogEntry(Guid.NewGuid(), "dynamic-agent", "dynamic", "Dynamic", "content", "Use persona.", "{}", true, "sales");
        var planner = FakePlanner(SingleTaskPlan("dynamic"));
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new[] { definition });
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new RagChunk("v1", "sales", "KB fact", 0.9f) });
        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("dynamic-ok", 11, 7, 0.02m, "test-model"));
        var tracker = AllowedTracker();
        var sut = Build(planner, Registry(), tracker: tracker, catalog: catalog, rag: rag, chat: chat);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        await rag.Received(1).RetrieveAsync(Arg.Is<RagRequest>(r => r.TenantId == Tenant && r.KbModuleCode == "sales" && r.Query == "Do"), Arg.Any<CancellationToken>());
        await chat.Received(1).CompleteAsync(
            Arg.Is<string>(prompt => prompt.Contains("Use persona.") && prompt.Contains("KB fact") && prompt.Contains("untrusted reference data")),
            Arg.Any<IReadOnlyList<ChatTurn>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await tracker.Received(1).RecordAsync(
            Arg.Is<CostEntry>(entry => entry.TenantId == Tenant && entry.AgentCode == "dynamic" && entry.InputTokens == 11 && entry.OutputTokens == 7 && entry.UsdCost == 0.02m),
            Arg.Any<CancellationToken>());
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
    public async Task RunAsync_RetriesTask_OnTransientFailure_WithoutBurningReplan()
    {
        // EARS[WHEN a delegated task suffers a transient failure THE SYSTEM SHALL retry the same task without replanning]
        // "flaky-agent" is intentionally NOT in the catalog so the orchestrator resolves the registry fake (not the
        // GenericLlmAgentWorker that data-defined agents shadow to).
        var planner = FakePlanner(SingleTaskPlan("flaky-agent"));
        var agent = TransientThenOkAgent("flaky-agent", 1, new TimeoutException("llm timeout"));
        var registry = Registry(agent);
        var sut = Build(planner, registry, new AutonomousOrchestratorOptions { MaxRounds = 1, TransientBackoffBaseMs = 0 });

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        agent.Calls.Should().Be(2);
        await planner.DidNotReceive().ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TreatsTimeoutInducedCancellation_AsTransientAndRetries()
    {
        // HttpClient.Timeout throws TaskCanceledException when the user ct is NOT the cause -> must retry, not abort.
        var planner = FakePlanner(SingleTaskPlan("flaky-agent"));
        var agent = TransientThenOkAgent("flaky-agent", 1, new TaskCanceledException("HttpClient.Timeout"));
        var registry = Registry(agent);
        var sut = Build(planner, registry, new AutonomousOrchestratorOptions { MaxRounds = 1, TransientBackoffBaseMs = 0 });

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        agent.Calls.Should().Be(2);
        await planner.DidNotReceive().ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TransientExhausted_FailsAndReplans()
    {
        // EARS[WHEN transient retries are exhausted THE SYSTEM SHALL surface the task as failed and replan]
        var planner = FakePlanner(SingleFailingPlan(), SingleFailingPlan());
        var agent = TransientThenOkAgent("failing-agent", 99, new TimeoutException("persistent timeout"));
        var registry = Registry(agent);
        var sut = Build(planner, registry, new AutonomousOrchestratorOptions { MaxRounds = 2, MaxTransientRetries = 1, TransientBackoffBaseMs = 0 });

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("failed");
        result.Reason.Should().Be("max_rounds");
        await planner.Received(2).ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ReActWorker_InvokesAllowedTool_ThenFinalAnswer()
    {
        // EARS[WHEN a data-defined agent declares allowed tools THE SYSTEM SHALL run a ReAct loop: the model emits
        // a JSON action, the worker invokes the allowed tool (forwarding tenant_id), feeds the observation back, and
        // returns the model's plain-text final answer]
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "dyn-content", "dyn", "Dyn", "content", "Generate then report.", "{}", true, null,
            """["content-agent"]""");
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new[] { definition });
        var planner = FakePlanner(SingleTaskPlan("dyn-content"));

        var contentTool = new CapturingFakeAgent("content-agent", new AgentResult("t1", true, "{\"post\":\"hello\"}", null));
        var toolRegistry = ToolRegistryFactory.Build(new[] { contentTool });

        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new ClaudeReply("""{"tool":"content-agent","args":{"platform":"facebook","brief":"x"}}""", 1, 2, 0m),
                new ClaudeReply("Final answer: posted", 3, 4, 0m));

        var sut = Build(planner, Registry(), catalog: catalog, chat: chat, toolRegistry: toolRegistry);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        contentTool.Calls.Should().Be(1);
        contentTool.LastInput.Should().ContainKey("platform").WhoseValue.Should().Be("facebook");
        contentTool.LastInput.Should().ContainKey("tenant_id").WhoseValue.Should().Be(Tenant.ToString("D"));
        await chat.Received(2).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ReActWorker_RejectsOutOfListTool()
    {
        // EARS[WHEN the model emits a tool action for a tool NOT in the agent's allow-list THE SYSTEM SHALL refuse
        // to invoke it and feed a not-available observation back so the model must use only allowed tools]
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "dyn-content", "dyn", "Dyn", "content", "Generate then report.", "{}", true, null,
            """["content-agent"]""");
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new[] { definition });
        var planner = FakePlanner(SingleTaskPlan("dyn-content"));

        var contentTool = new CapturingFakeAgent("content-agent", new AgentResult("t1", true, "{\"post\":\"hello\"}", null));
        var toolRegistry = ToolRegistryFactory.Build(new[] { contentTool });

        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new ClaudeReply("""{"tool":"ads-agent","args":{}}""", 1, 2, 0m),
                new ClaudeReply("Final: ads not available, content skipped", 3, 4, 0m));

        var sut = Build(planner, Registry(), catalog: catalog, chat: chat, toolRegistry: toolRegistry);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        contentTool.Calls.Should().Be(0); // ads-agent is not in the allow-list -> never invoked
    }

    [Fact]
    public async Task RunAsync_EmitsReadableRunSummary_OnCompletion()
    {
        // SPEC-16 P2-11: on completion the orchestrator posts a human-readable run_summary trace composed from the sub-agent results.
        var planner = FakePlanner(TwoStepPlan());
        var registry = Registry(
            OkAgent("research-agent", "insight"),
            OkAgent("content-agent", "draft"));
        var sink = Substitute.For<IAutonomousRunSink>();
        var capturedPhases = new System.Collections.Concurrent.ConcurrentBag<string>();
        sink.When(s => s.TraceAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()))
            .Do(ci => capturedPhases.Add(ci.ArgAt<string>(4)));
        var sut = Build(planner, registry, sink: sink);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        capturedPhases.Should().Contain("plan_summary");
        capturedPhases.Should().Contain("run_summary");
    }

    [Fact]
    public async Task RunAsync_HighRiskTool_RefusedWhenApprovalRequired()
    {
        // EARS[WHEN a high-risk tool is invoked and the tenant requires approval THE SYSTEM SHALL refuse it
        // (the model gets a needs-approval observation and never executes the high-risk action)]
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "dyn-pub", "dyn", "Dyn", "publisher", "Publish content.", "{}", true, null,
            """["content.publish"]""");
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new[] { definition });
        var planner = FakePlanner(SingleTaskPlan("dyn-pub"));

        var highRiskTool = new HighRiskFakeTool("content.publish");
        var toolRegistry = ToolRegistryFactory.Build(Array.Empty<IAgent>(), new[] { highRiskTool });

        var approvalResolver = Substitute.For<IOrchestrationApprovalResolver>();
        approvalResolver.IsRequiredAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new ClaudeReply("""{"tool":"content.publish","args":{"content_id":"x"}}""", 1, 2, 0m),
                new ClaudeReply("Final: publish needs approval", 3, 4, 0m));

        var sut = Build(planner, Registry(), catalog: catalog, chat: chat, toolRegistry: toolRegistry, approvalResolver: approvalResolver);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        highRiskTool.Calls.Should().Be(0); // high-risk tool never executed
    }

    [Fact]
    public async Task RunAsync_HighRiskTool_AllowedWhenApprovalNotRequired()
    {
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "dyn-pub", "dyn", "Dyn", "publisher", "Publish content.", "{}", true, null,
            """["content.publish"]""");
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new[] { definition });
        var planner = FakePlanner(SingleTaskPlan("dyn-pub"));

        var highRiskTool = new HighRiskFakeTool("content.publish");
        var toolRegistry = ToolRegistryFactory.Build(Array.Empty<IAgent>(), new[] { highRiskTool });

        var approvalResolver = Substitute.For<IOrchestrationApprovalResolver>();
        approvalResolver.IsRequiredAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new ClaudeReply("""{"tool":"content.publish","args":{"content_id":"x"}}""", 1, 2, 0m),
                new ClaudeReply("Final: published", 3, 4, 0m));

        var sut = Build(planner, Registry(), catalog: catalog, chat: chat, toolRegistry: toolRegistry, approvalResolver: approvalResolver);

        var result = await sut.RunAsync(Request("manual"), CancellationToken.None);

        result.Status.Should().Be("completed");
        highRiskTool.Calls.Should().Be(1); // toggle off -> executed
    }

    [Fact]
    public async Task RunAsync_DryRun_PreviewsToolActions_WithoutExecuting()
    {
        // EARS[WHEN dry-run is on THE SYSTEM SHALL return tool-action previews without side effects]
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "dyn-content", "dyn", "Dyn", "content", "Generate then report.", "{}", true, null,
            """["content-agent"]""");
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new[] { definition });
        var planner = FakePlanner(SingleTaskPlan("dyn-content"));

        var contentTool = new CapturingFakeAgent("content-agent", new AgentResult("t1", true, "{\"post\":\"hello\"}", null));
        var toolRegistry = ToolRegistryFactory.Build(new[] { contentTool });

        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new ClaudeReply("""{"tool":"content-agent","args":{"platform":"facebook","brief":"x"}}""", 1, 2, 0m),
                new ClaudeReply("Final: previewed", 3, 4, 0m));

        var dryRunRequest = new AutonomousRunRequest(Tenant, Session, "Launch HSK4 campaign", "manual", RequiresApproval: false, DryRun: true);
        var sut = Build(planner, Registry(), catalog: catalog, chat: chat, toolRegistry: toolRegistry);

        var result = await sut.RunAsync(dryRunRequest, CancellationToken.None);

        result.Status.Should().Be("completed");
        // Adapter-wrapped tool short-circuited on dry-run -> the underlying adapter never executed.
        contentTool.Calls.Should().Be(0);
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
        ILlmCostTracker? tracker = null,
        IAgentDefinitionCatalog? catalog = null,
        IRagRetriever? rag = null,
        IClaudeChatClient? chat = null,
        ToolRegistry? toolRegistry = null,
        IAutonomousRunSink? sink = null,
        IOrchestrationApprovalResolver? approvalResolver = null)
    {
        tracker ??= AllowedTracker();
        if (catalog is null)
        {
            catalog = Substitute.For<IAgentDefinitionCatalog>();
            catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new[] { Research, Content });
        }
        var mailbox = Substitute.For<IA2AMailbox>();
        sink ??= Substitute.For<IAutonomousRunSink>();
        rag ??= Substitute.For<IRagRetriever>();
        if (chat is null)
        {
            chat = Substitute.For<IClaudeChatClient>();
            chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new ClaudeReply("ok", 0, 0, 0m));
        }
        return new AutonomousOrchestrator(
            planner, catalog, registry, mailbox,
            new OrchestratorCostGuard(tracker),
            new LlmCallScope(), sink, rag, chat, new FixedClock(Now), options, toolRegistry, approvalResolver);
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

    private static TransientThenOkFakeAgent TransientThenOkAgent(string name, int transientCount, Exception ex) =>
        new TransientThenOkFakeAgent(name, transientCount, ex);

    // Real fake (not NSubstitute): throwing from a NSubstitute Returns-callback wraps the exception and breaks
    // transient classification, so fault the Task directly with the original exception type intact.
    private sealed class TransientThenOkFakeAgent : IAgent
    {
        private readonly int _transientCount;
        private readonly Exception _ex;
        private int _calls;

        public TransientThenOkFakeAgent(string name, int transientCount, Exception ex)
        {
            Name = name;
            _transientCount = transientCount;
            _ex = ex;
        }

        public string Name { get; }
        public int Calls => _calls;

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken cancellationToken) =>
            ++_calls <= _transientCount
                ? Task.FromException<AgentResult>(_ex)
                : Task.FromResult(new AgentResult(task.Id, true, "recovered", null));
    }

    // Minimal high-risk tool fake for the P4-4 risk-gate test.
    private sealed class HighRiskFakeTool : IAgentTool
    {
        private int _calls;
        public HighRiskFakeTool(string name) { Name = name; }
        public string Name { get; }
        public string Description => "publish";
        public string InputSchemaJson => "{}";
        public string RequiredPermission => "content:write";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.High;
        public int Calls => _calls;
        public Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
        {
            _calls++;
            return Task.FromResult(ToolResult.Ok("{\"published\":true}"));
        }
    }

    // Captures the last task input so a test can assert the tool forwarded args + tenant_id into the adapter.
    private sealed class CapturingFakeAgent : IAgent
    {
        private readonly AgentResult _result;
        private int _calls;

        public CapturingFakeAgent(string name, AgentResult result)
        {
            Name = name;
            _result = result;
        }

        public string Name { get; }
        public int Calls => _calls;
        public IReadOnlyDictionary<string, string> LastInput { get; private set; } = new Dictionary<string, string>(0);

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken cancellationToken)
        {
            ++_calls;
            LastInput = task.Input;
            return Task.FromResult(_result);
        }
    }

    private static ILlmCostTracker AllowedTracker()
    {
        var t = Substitute.For<ILlmCostTracker>();
        t.SummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CostSummary(Tenant, 0m, 1000m, 0));
        return t;
    }

    private static ILlmCostTracker DeniedTracker()
    {
        var t = Substitute.For<ILlmCostTracker>();
        t.SummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CostSummary(Tenant, 1000m, 1m, 100));
        return t;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
