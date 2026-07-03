using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
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
    public async Task Submit_persists_planned_started_and_completed_trace_phases()
    {
        using var harness = Harness.Build(OneTaskPlan);

        await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        harness.Db.AgentTraces.Select(t => t.Phase).Should().Contain(["planning_completed", "started", "completed"]);
    }

    [Fact]
    public async Task Submit_persists_failed_session_and_trace_when_planner_returns_invalid_json()
    {
        using var harness = Harness.Build("not-json");

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        response.SessionId.Should().NotBeNullOrEmpty();
        response.Status.Should().Be(AgentSessionStatuses.Failed);
        harness.Db.AgentSessions.Should().ContainSingle();
        harness.Db.AgentTraces.Select(t => t.Phase).Should().Contain(["planning_started", "planning_failed"]);
    }

    [Fact]
    public async Task Submit_persists_provider_auth_failure_trace_when_planner_is_forbidden()
    {
        using var harness = Harness.Build(new HttpRequestException(
            "Forbidden: raw provider diagnostic token=secret",
            null,
            System.Net.HttpStatusCode.Forbidden));

        var response = await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        response.Status.Should().Be(AgentSessionStatuses.Failed);
        var failure = harness.Db.AgentTraces.Should().ContainSingle(trace => trace.Phase == "planning_failed").Subject;
        failure.Message.Should().Contain("LLM của orchestrator bị từ chối (401/403)");
        failure.Message.Should().NotContain("raw provider diagnostic");
        failure.Message.Should().NotContain("secret");
    }

    [Fact]
    public async Task Submit_attributes_session_to_orchestrator_agent_so_traces_surface_on_dashboard()
    {
        using var harness = Harness.Build(OneTaskPlan);
        var orchestrator = AgentConfig.Create(TenantId, "orchestrator", "Agent-Orchestrator", "planner", "test-model", DateTimeOffset.UnixEpoch);
        harness.Db.AgentConfigs.Add(orchestrator);
        await harness.Db.SaveChangesAsync();

        await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        harness.Db.AgentSessions.Single().AgentId.Should().Be(orchestrator.Id);
    }

    [Fact]
    public async Task Submit_persists_planning_started_trace_before_running_the_plan()
    {
        using var harness = Harness.Build(OneTaskPlan);

        await harness.Service.Submit(new SubmitRequest
        {
            TenantId = TenantId.ToString("D"),
            Goal = "launch HSK4 campaign",
        }, TestServerCallContext.Create());

        harness.Db.AgentTraces.Select(t => t.Phase)
            .Should().Contain(["planning_started", "planning_completed", "completed"]);
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
                new CostSummary(TenantId, 1m, 200m, 0.005f),
                new CostSummary(TenantId, 200m, 200m, 1f),
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

        public static Harness Build(Exception plannerError)
        {
            var dbHarness = new AgentServiceTestAppDb(TenantId);
            return Build(OneTaskPlan, new FakeContentAdapter("content-ok"), dbHarness, plannerError: plannerError);
        }

        public static Harness Build(
            string planJson,
            IAgent adapter,
            AgentServiceTestAppDb dbHarness,
            ILlmCostTracker? tracker = null,
            DateTimeOffset? clockAt = null,
            ILlmCallScope? llmScope = null,
            Exception? plannerError = null)
        {
            var catalog = new FakeCatalog();
            var planGen = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(new FixedChatClient(planJson, plannerError)));
            var costGuard = new OrchestratorCostGuard(tracker ?? new FixedTracker());
            var adapters = new IAgent[] { adapter };
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(clockAt ?? new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero));
            var llmScopeValue = llmScope ?? new LlmCallScope();
            var redactor = new RegexPiiRedactor();
            var definitionCatalog = new FakeDefinitionCatalog();
            var rag = Substitute.For<IRagRetriever>();
            var chat = new AdapterBackedChatClient(adapter, llmScopeValue);
            var autonomous = new AutonomousOrchestrator(
                new AutonomousPlanner(planGen, llmScopeValue),
                definitionCatalog,
                new AgentRegistry(adapters),
                Substitute.For<IA2AMailbox>(),
                costGuard,
                llmScopeValue,
                new AutonomousRunSink(dbHarness.Db, redactor, clock),
                rag,
                chat,
                clock);

            var service = new OrchestratorGrpcService(
                planGen, autonomous, catalog, adapters, llmScopeValue, redactor, costGuard,
                dbHarness.Db, clock, NullLogger<OrchestratorGrpcService>.Instance);

            return new Harness(dbHarness, service);
        }

        public void Dispose() => _dbHarness.Dispose();
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

    private sealed class FakeDefinitionCatalog : IAgentDefinitionCatalog
    {
        private static readonly AgentDefinitionCatalogEntry Content = new(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "content-agent", "content", "Content", "content", "Run content", "{}", true, null);

        public Task<IReadOnlyList<AgentDefinitionCatalogEntry>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentDefinitionCatalogEntry>>([Content]);

        public Task<AgentDefinitionCatalogEntry?> FindByCodeAsync(Guid tenantId, string code, CancellationToken ct = default) =>
            Task.FromResult<AgentDefinitionCatalogEntry?>(string.Equals(code, Content.Code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, Content.ShortName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, Content.AgentType, StringComparison.OrdinalIgnoreCase)
                    ? Content
                    : null);
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

    private sealed class FixedTracker : ILlmCostTracker
    {
        public string Name => "cost";
        public Task RecordAsync(CostEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct) =>
            Task.FromResult(new CostSummary(tenantId, 1m, 200m, 0.005f));
    }

    private sealed class SequencedTracker(IReadOnlyList<CostSummary> summaries) : ILlmCostTracker
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

    private sealed class AdapterBackedChatClient(IAgent adapter, ILlmCallScope scope) : IClaudeChatClient
    {
        public async Task<ClaudeReply> CompleteAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            var agentCode = scope.Current?.AgentCode ?? adapter.Name;
            var input = ExtractInput(userMessage);
            var result = await adapter.ExecuteAsync(new AgentTask("t1", agentCode, "Do", input), ct).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "agent_failed");
            return new ClaudeReply(result.Output, 0, 0, 0m, "test");
        }

        private static Dictionary<string, string> ExtractInput(string userMessage)
        {
            var marker = "Input JSON:";
            var index = userMessage.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return new Dictionary<string, string>();

            var json = userMessage[(index + marker.Length)..].Trim();
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct).ConfigureAwait(false);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, reply.InputTokens, reply.OutputTokens, reply.UsdCost, reply.Model);
        }
    }

    private sealed class FixedChatClient(string response, Exception? error = null) : IClaudeChatClient
    {
        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            error is null
                ? Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"))
                : Task.FromException<ClaudeReply>(error);

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct).ConfigureAwait(false);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, reply.InputTokens, reply.OutputTokens, reply.UsdCost, reply.Model);
        }
    }
}
