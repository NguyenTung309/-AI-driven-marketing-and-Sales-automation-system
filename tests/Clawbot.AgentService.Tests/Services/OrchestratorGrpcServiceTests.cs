using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Tenants;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class OrchestratorGrpcServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private const string OneTaskPlan = """
    { "version": 1, "tasks": [ { "id": "t1", "agent": "content", "description": "write post", "input": {}, "dependsOn": [], "status": "pending" } ] }
    """;

    private const string TwoTaskPlan = """
    { "version": 1, "tasks": [
      { "id": "t1", "agent": "content", "description": "outline", "input": {}, "dependsOn": [], "status": "pending" },
      { "id": "t2", "agent": "content", "description": "write", "input": {}, "dependsOn": ["t1"], "status": "pending" }
    ] }
    """;

    private const string PiiPlan = """
    { "version": 1, "tasks": [
      { "id": "t1", "agent": "content", "description": "call 0912345678", "input": { "phone": "0912345678", "brief": "HSK4" }, "dependsOn": [], "status": "pending" }
    ] }
    """;

    [Fact]
    public async Task Plan_and_trace_stream_planned_events_for_session()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var service = harness.Service;

        var plan = await service.Plan(new PlanRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "chat with learner",
        }, TestServerCallContext.Create());

        plan.Tasks.Should().ContainSingle(task => task.Agent == "chat");

        var stream = new CapturingTraceStream();
        await service.Trace(new TraceRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = plan.SessionId,
        }, stream, TestServerCallContext.Create());

        stream.Messages.Should().ContainSingle();
        stream.Messages[0].Phase.Should().Be("planned");
        stream.Messages[0].Message.Should().Contain("chat");
    }

    [Fact]
    public async Task Submit_auto_runs_plan_to_completion()
    {
        using var harness = Harness.Build(OneTaskPlan);

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        response.Status.Should().Be(AgentSessionStatuses.Completed);
        response.RequiresApproval.Should().BeFalse();
        response.Tasks.Should().ContainSingle();
        response.Tasks[0].Status.Should().Be("completed");
        response.Tasks[0].Output.Should().Be("content-ok");
    }

    [Fact]
    public void Lifecycle_contract_exposes_full_editable_plan_payload()
    {
        SessionResponse.Descriptor.FindFieldByName("plan_json").Should().NotBeNull();
        PlannedTask.Descriptor.FindFieldByName("input_json").Should().NotBeNull();
    }

    [Fact]
    public async Task Submit_returns_plan_json_and_task_input_for_review_edit_flow()
    {
        using var harness = Harness.Build(PiiPlan);

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "prepare learner outreach",
        }, TestServerCallContext.Create());

        var planJson = (string)SessionResponse.Descriptor.FindFieldByName("plan_json")!.Accessor.GetValue(response);
        var inputJson = (string)PlannedTask.Descriptor.FindFieldByName("input_json")!.Accessor.GetValue(response.Tasks[0]);

        planJson.Should().Contain("tasks");
        inputJson.Should().Contain("brief");
        inputJson.Should().NotContain("0912345678");
    }

    [Fact]
    public async Task Trace_streams_persisted_dynamic_session_traces()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var submitted = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        var stream = new CapturingTraceStream();
        await harness.Service.Trace(new TraceRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = submitted.SessionId,
        }, stream, TestServerCallContext.Create());

        stream.Messages.Should().Contain(message => message.TaskId == "t1" && message.Phase == "completed");
    }

    [Fact]
    public async Task Submit_persists_planned_started_and_completed_trace_phases()
    {
        using var harness = Harness.Build(OneTaskPlan);

        await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        harness.Db.AgentTraces.Select(t => t.Phase).Should().Contain(["planned", "started", "completed"]);
    }

    [Fact]
    public async Task Control_cancel_rejects_pending_approval_plan()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var tenant = Tenant.Create("acme", "Acme", "free", DateTimeOffset.UnixEpoch);
        SetTenantId(tenant, TenantId);
        tenant.SetRequireOrchestrationApproval(true);
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();
        var submitted = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        var act = async () => await harness.Service.Control(new ControlRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = submitted.SessionId,
            Action = "cancel",
            ExpectedEtag = submitted.Etag,
        }, TestServerCallContext.Create());

        var error = (await act.Should().ThrowAsync<RpcException>()).Which;
        error.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task Submit_stops_at_pending_approval_when_tenant_requires_it()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var tenant = Tenant.Create("acme", "Acme", "free", DateTimeOffset.UnixEpoch);
        SetTenantId(tenant, TenantId);
        tenant.SetRequireOrchestrationApproval(true);
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        response.Status.Should().Be(AgentSessionStatuses.PendingApproval);
        response.RequiresApproval.Should().BeTrue();
        response.Tasks[0].Status.Should().Be("pending");
    }

    [Fact]
    public async Task Approve_runs_a_pending_plan()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var tenant = Tenant.Create("acme", "Acme", "free", DateTimeOffset.UnixEpoch);
        SetTenantId(tenant, TenantId);
        tenant.SetRequireOrchestrationApproval(true);
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();

        var submitted = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        var approved = await harness.Service.Approve(new SessionRef
        {
            TenantId = TenantId.ToString("D"),
            SessionId = submitted.SessionId,
            ExpectedEtag = submitted.Etag,
        }, TestServerCallContext.Create());

        approved.Status.Should().Be(AgentSessionStatuses.Completed);
        approved.Tasks[0].Status.Should().Be("completed");
    }

    [Fact]
    public async Task Control_cancel_marks_paused_session_cancelled()
    {
        var dbHarness = new AgentServiceTestAppDb(TenantId);
        using var harness = Harness.Build(TwoTaskPlan, new PausingContentAdapter(dbHarness.Db), dbHarness);
        var paused = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "outline then write HSK4 launch",
        }, TestServerCallContext.Create());

        var cancelled = await harness.Service.Control(new ControlRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = paused.SessionId,
            Action = "cancel",
            ExpectedEtag = paused.Etag,
        }, TestServerCallContext.Create());

        cancelled.Status.Should().Be(AgentSessionStatuses.Cancelled);
    }

    [Fact]
    public async Task Submit_redacts_pii_from_persisted_goal()
    {
        using var harness = Harness.Build(OneTaskPlan);

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "Liên hệ học viên qua số 0912345678 để chốt đơn",
        }, TestServerCallContext.Create());

        response.Goal.Should().Contain("[PHONE]");
        response.Goal.Should().NotContain("0912345678");
    }

    [Fact]
    public async Task Submit_redacts_pii_from_persisted_task_output()
    {
        using var harness = Harness.Build(OneTaskPlan, adapterOutput: "call 0912345678 now");

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        response.Tasks[0].Output.Should().Contain("[PHONE]");
        response.Tasks[0].Output.Should().NotContain("0912345678");
        harness.Db.AgentSessions.Single().PlanJson.Should().NotContain("0912345678");
    }

    [Fact]
    public async Task UpdatePlan_redacts_pii_from_persisted_description_and_inputs()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var tenant = Tenant.Create("acme", "Acme", "free", DateTimeOffset.UnixEpoch);
        SetTenantId(tenant, TenantId);
        tenant.SetRequireOrchestrationApproval(true);
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();

        var submitted = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        await harness.Service.UpdatePlan(new UpdatePlanRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = submitted.SessionId,
            PlanJson = PiiPlan,
            ExpectedEtag = submitted.Etag,
        }, TestServerCallContext.Create());

        var planJson = harness.Db.AgentSessions.Single().PlanJson;
        planJson.Should().Contain("[PHONE]");
        planJson.Should().NotContain("0912345678");
    }

    [Fact]
    public async Task UpdatePlan_rejects_stale_etag()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var tenant = Tenant.Create("acme", "Acme", "free", DateTimeOffset.UnixEpoch);
        SetTenantId(tenant, TenantId);
        tenant.SetRequireOrchestrationApproval(true);
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();
        var submitted = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        var act = async () => await harness.Service.UpdatePlan(new UpdatePlanRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = submitted.SessionId,
            PlanJson = OneTaskPlan,
            ExpectedEtag = "stale",
        }, TestServerCallContext.Create());

        var error = (await act.Should().ThrowAsync<RpcException>()).Which;
        error.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        error.Status.Detail.Should().Be("etag_mismatch");
    }

    [Fact]
    public async Task UpdatePlan_requires_etag()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var tenant = Tenant.Create("acme", "Acme", "free", DateTimeOffset.UnixEpoch);
        SetTenantId(tenant, TenantId);
        tenant.SetRequireOrchestrationApproval(true);
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();
        var submitted = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        var act = async () => await harness.Service.UpdatePlan(new UpdatePlanRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = submitted.SessionId,
            PlanJson = OneTaskPlan,
        }, TestServerCallContext.Create());

        var error = (await act.Should().ThrowAsync<RpcException>()).Which;
        error.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        error.Status.Detail.Should().Be("etag_required");
    }

    [Fact]
    public async Task Submit_persists_trace_phases_without_duplicate_terminal_trace()
    {
        using var harness = Harness.Build(TwoTaskPlan);

        await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "outline then write HSK4 launch",
        }, TestServerCallContext.Create());

        harness.Db.AgentTraces.Count(t => t.Phase == "completed").Should().Be(2);
        harness.Db.AgentTraces.Count(t => t.TaskId == "t1" && t.Phase == "completed").Should().Be(1);
        harness.Db.AgentTraces.Count(t => t.TaskId == "t2" && t.Phase == "completed").Should().Be(1);
    }

    [Fact]
    public async Task Submit_runs_dependent_tasks_to_completion()
    {
        using var harness = Harness.Build(TwoTaskPlan);

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "outline then write HSK4 launch",
        }, TestServerCallContext.Create());

        response.Status.Should().Be(AgentSessionStatuses.Completed);
        response.Tasks.Should().HaveCount(2);
        response.Tasks.Should().OnlyContain(task => task.Status == "completed");
    }

    [Fact]
    public async Task Submit_marks_failed_when_midrun_cost_cap_is_hit()
    {
        var dbHarness = new AgentServiceTestAppDb(TenantId);
        using var harness = Harness.Build(
            TwoTaskPlan,
            new FakeContentAdapter("content-ok"),
            dbHarness,
            new SequencedTracker([
                new CostSummary(TenantId, 1m, 200m, 0.005f),
                new CostSummary(TenantId, 199.98m, 200m, 0.9999f),
            ]));

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "outline then write HSK4 launch",
        }, TestServerCallContext.Create());

        response.Status.Should().Be(AgentSessionStatuses.Failed);
        response.Tasks.Should().Contain(task => task.Error == "cost_cap_midrun");
    }

    [Fact]
    public async Task Submit_propagates_reservation_timestamp_to_nested_agent_cost_scope()
    {
        var dbHarness = new AgentServiceTestAppDb(TenantId);
        var scope = new LlmCallScope();
        var reservationAt = new DateTimeOffset(2026, 6, 30, 23, 59, 0, TimeSpan.Zero);
        var adapter = new CostAtCapturingAdapter(scope);
        using var harness = Harness.Build(OneTaskPlan, adapter, dbHarness, clockAt: reservationAt, llmScope: scope);

        await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        adapter.CapturedCostAt.Should().Be(reservationAt);
    }

    [Fact]
    public async Task Submit_pauses_between_tasks_without_failing_pending_work()
    {
        var dbHarness = new AgentServiceTestAppDb(TenantId);
        using var harness = Harness.Build(TwoTaskPlan, new PausingContentAdapter(dbHarness.Db), dbHarness);

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "outline then write HSK4 launch",
        }, TestServerCallContext.Create());

        response.Status.Should().Be(AgentSessionStatuses.Paused);
        response.Tasks[0].Status.Should().Be("completed");
        response.Tasks[1].Status.Should().Be("pending");
        response.Tasks[1].Error.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Control_resume_continues_paused_plan_from_pending_tasks()
    {
        var dbHarness = new AgentServiceTestAppDb(TenantId);
        using var harness = Harness.Build(TwoTaskPlan, new PausingContentAdapter(dbHarness.Db), dbHarness);
        var paused = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "outline then write HSK4 launch",
        }, TestServerCallContext.Create());

        var resumed = await harness.Service.Control(new ControlRequest
        {
            TenantId = TenantId.ToString("D"),
            SessionId = paused.SessionId,
            Action = "resume",
            ExpectedEtag = paused.Etag,
        }, TestServerCallContext.Create());

        resumed.Status.Should().Be(AgentSessionStatuses.Completed);
        resumed.Tasks.Should().OnlyContain(task => task.Status == "completed");
    }

    [Fact]
    public async Task Submit_overrides_plan_tenant_id_before_agent_execution()
    {
        const string plan = """
        { "version": 3, "tasks": [
          { "id": "t1", "agent": "content", "description": "content", "input": { "tenant_id": "11111111-1111-1111-1111-111111111111", "platform": "facebook", "brief": "HSK4" }, "dependsOn": [], "status": "pending" }
        ] }
        """;
        var adapter = new CapturingInputAdapter();
        using var harness = Harness.Build(plan, adapter, new AgentServiceTestAppDb(TenantId));

        await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        adapter.Input["tenant_id"].Should().Be(TenantId.ToString("D"));
    }

    [Fact]
    public async Task GetPlan_returns_not_found_for_unknown_session()
    {
        using var harness = Harness.Build(OneTaskPlan);

        var act = async () => await harness.Service.GetPlan(new SessionRef
        {
            TenantId = TenantId.ToString("D"),
            SessionId = Guid.NewGuid().ToString("D"),
        }, TestServerCallContext.Create());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    private static void SetTenantId(Tenant tenant, Guid id) =>
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, id);

    private sealed class Harness : IDisposable
    {
        private readonly AgentServiceTestAppDb _dbHarness;
        public OrchestratorGrpcService Service { get; }
        public Clawbot.Infrastructure.Persistence.AppDbContext Db => _dbHarness.Db;

        private Harness(AgentServiceTestAppDb dbHarness, OrchestratorGrpcService service)
        {
            _dbHarness = dbHarness;
            Service = service;
        }

        public static Harness Build(string planJson, string adapterOutput = "content-ok")
        {
            var dbHarness = new AgentServiceTestAppDb(TenantId);
            return Build(planJson, new FakeContentAdapter(adapterOutput), dbHarness);
        }

        public static Harness Build(
            string planJson,
            IAgent adapter,
            AgentServiceTestAppDb dbHarness,
            IClaudeCostTracker? tracker = null,
            DateTimeOffset? clockAt = null,
            ILlmCallScope? llmScope = null)
        {
            var catalog = new FakeCatalog();
            var planGen = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(new FixedChatClient(planJson)));
            var costGuard = new OrchestratorCostGuard(tracker ?? new FixedTracker());
            var sk = new SemanticKernelOrchestrator(catalog, planGen, new RegexPiiRedactor(), costGuard);
            var legacy = new PlanningOrchestrator(new AgentRegistry([ChatAgentStub()]));
            var adapters = new IAgent[] { adapter };
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(clockAt ?? new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero));

            var redactor = new RegexPiiRedactor();
            var service = new OrchestratorGrpcService(
                legacy, sk, planGen, catalog, adapters, llmScope ?? new LlmCallScope(), redactor, costGuard,
                dbHarness.Db, clock, NullLogger<OrchestratorGrpcService>.Instance);

            return new Harness(dbHarness, service);
        }

        public void Dispose() => _dbHarness.Dispose();

        private static IAgent ChatAgentStub()
        {
            var agent = Substitute.For<IAgent>();
            agent.Name.Returns("chat");
            return agent;
        }
    }

    private sealed class FakeCatalog : IAgentCatalog
    {
        private static readonly AgentCatalogEntry Content = new(
            "content-agent", "content", "Content", "content", "Run content", "{}", Orchestratable: true);

        public Task<IReadOnlyList<AgentCatalogEntry>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentCatalogEntry>>([Content]);

        public Task<AgentCatalogEntry> ResolveAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(Content);
    }

    private sealed class FakeContentAdapter(string output) : IAgent
    {
        public string Name => "content-agent";

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct) =>
            Task.FromResult(new AgentResult(task.Id, Success: true, Output: output, Error: null));
    }

    private sealed class CapturingInputAdapter : IAgent
    {
        public string Name => "content-agent";
        public IReadOnlyDictionary<string, string> Input { get; private set; } = new Dictionary<string, string>();

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
        {
            Input = task.Input;
            return Task.FromResult(new AgentResult(task.Id, Success: true, Output: "content-ok", Error: null));
        }
    }

    private sealed class CostAtCapturingAdapter(ILlmCallScope scope) : IAgent
    {
        public string Name => "content-agent";
        public DateTimeOffset? CapturedCostAt { get; private set; }

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
        {
            using (scope.Begin(TenantId, Name))
            {
                CapturedCostAt = scope.Current?.CostAt;
            }

            return Task.FromResult(new AgentResult(task.Id, Success: true, Output: "content-ok", Error: null));
        }
    }

    private sealed class PausingContentAdapter(Clawbot.Infrastructure.Persistence.AppDbContext db) : IAgent
    {
        private int _calls;
        public string Name => "content-agent";

        public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                var session = db.AgentSessions.Single();
                session.Pause();
                await db.SaveChangesAsync(ct);
                return new AgentResult(task.Id, Success: true, Output: "first-ok", Error: null);
            }

            return new AgentResult(task.Id, Success: true, Output: "should-not-run", Error: null);
        }
    }

    private sealed class FixedTracker : IClaudeCostTracker
    {
        public string Name => "cost";
        public Task RecordAsync(CostEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct) =>
            Task.FromResult(new CostSummary(tenantId, 1m, 200m, 0.005f));
    }

    private sealed class SequencedTracker(IReadOnlyList<CostSummary> summaries) : IClaudeCostTracker
    {
        private int _index;
        public string Name => "cost";
        public Task RecordAsync(CostEntry entry, CancellationToken ct) => Task.CompletedTask;

        public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct)
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, summaries.Count - 1);
            return Task.FromResult(summaries[index]);
        }
    }

    private sealed class FixedChatClient(string response) : IClaudeChatClient
    {
        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"));

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ClaudeStreamChunk(response, Final: true, 1, 1, 0.01m, "test");
        }
    }

    private sealed class CapturingTraceStream : IServerStreamWriter<TraceEvent>
    {
        public List<TraceEvent> Messages { get; } = [];
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(TraceEvent message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
