using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Orchestrator;

// Chính sách "pause" nay là review gate: mỗi bước hoàn tất phải chờ người duyệt/sửa trước khi
// output trở thành input của bước sau. Task lỗi vẫn để AI lập kế hoạch lại.
public sealed class OrchestratorFailurePolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public async Task Pause_PausesAfterEachCompletedNonFinalTask()
    {
        var harness = new Harness(SucceedingAgent("worker", "output t1"));
        var plan = PlanOf(
            Task("t1", "worker"),
            Task("t2", "worker", dependsOn: ["t1"]));

        var result = await harness.RunAsync(plan);

        result.Status.Should().Be("paused");
        result.Reason.Should().Be("awaiting_approval");
        harness.LastPersistedPlan!.Tasks.Should().ContainSingle(task => task.Id == "t1" && task.Status == "completed");
        harness.LastPersistedPlan.Tasks.Should().ContainSingle(task => task.Id == "t2" && task.Status == "pending");
        await harness.Sink.Received(1).PauseForInterventionAsync(
            TenantId, SessionId, "t1", "task_completed_awaiting_approval", Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_DoesNotPauseAfterFinalTask()
    {
        var harness = new Harness(SucceedingAgent("worker", "output t1"));

        var result = await harness.RunAsync(PlanOf(Task("t1", "worker")));

        result.Status.Should().Be("completed");
        await harness.Sink.DidNotReceiveWithAnyArgs().PauseForInterventionAsync(
            default, default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Pause_FailedTask_TriggersReplanInsteadOfWaitingForIntervention()
    {
        var harness = new Harness(FailingAgent("worker", "quota_exhausted"));
        harness.Planner
            .ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>())
            .Returns(PlanOf(Task("t1", "worker")));

        var result = await harness.RunAsync(PlanOf(Task("t1", "worker")));

        result.Status.Should().Be("failed");
        result.Reason.Should().Be("max_rounds");
        await harness.Planner.ReceivedWithAnyArgs(1).ReplanAsync(default, default!, default!, default!, default);
        await harness.Sink.DidNotReceiveWithAnyArgs().PauseForInterventionAsync(
            default, default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task FailedTask_StillReplans_WhenTenantChoosesReplanPolicy()
    {
        var harness = new Harness(FailingAgent("worker", "quota_exhausted"), policy: OrchestratorFailurePolicies.Replan);
        harness.Planner
            .ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>())
            .Returns(PlanOf(Task("t1", "worker")));

        var result = await harness.RunAsync(PlanOf(Task("t1", "worker")));

        result.Status.Should().Be("failed");
        result.Reason.Should().Be("max_rounds");
        await harness.Planner.ReceivedWithAnyArgs(1).ReplanAsync(default, default!, default!, default!, default);
        await harness.Sink.DidNotReceiveWithAnyArgs().PauseForInterventionAsync(
            default, default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task FailedTask_FailsTheWholeRun_WhenTenantChoosesFailPolicy()
    {
        var harness = new Harness(FailingAgent("worker", "quota_exhausted"), policy: OrchestratorFailurePolicies.Fail);

        var result = await harness.RunAsync(PlanOf(Task("t1", "worker")));

        result.Status.Should().Be("failed");
        result.Reason.Should().Be("task_failed");
        await harness.Planner.DidNotReceiveWithAnyArgs().ReplanAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task ScheduledRun_ReplansOnceThenFails_InsteadOfWaitingForAHumanThatIsNotThere()
    {
        // Lịch chạy lúc 3h sáng: "pause" sẽ treo phiên tới khi có người mở dashboard. Đổi thành replan
        // đúng một lượt, hết lượt thì fail.
        var harness = new Harness(FailingAgent("worker", "quota_exhausted"), source: "schedule");
        harness.Planner
            .ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>())
            .Returns(PlanOf(Task("t1", "worker")));

        var result = await harness.RunAsync(PlanOf(Task("t1", "worker")));

        result.Status.Should().Be("failed");
        result.Reason.Should().Be("max_rounds");
        await harness.Planner.ReceivedWithAnyArgs(1).ReplanAsync(default, default!, default!, default!, default);
        await harness.Sink.DidNotReceiveWithAnyArgs().PauseForInterventionAsync(
            default, default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task ScheduledRun_StillFailsOutright_WhenTenantChoosesFailPolicy()
    {
        // "fail" là lựa chọn chặt hơn "pause"; nới nó thành replan là tự ý tiêu tiền của tenant.
        var harness = new Harness(FailingAgent("worker", "quota_exhausted"), policy: OrchestratorFailurePolicies.Fail, source: "schedule");

        var result = await harness.RunAsync(PlanOf(Task("t1", "worker")));

        result.Reason.Should().Be("task_failed");
        await harness.Planner.DidNotReceiveWithAnyArgs().ReplanAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task ManualRunFromASchedule_ReplansFailedTaskInsteadOfWaitingForAnEdit()
    {
        // AgentScheduleRunner gắn source "manual" cho lần bấm chạy tay từ màn lịch. "pause" ở đây là
        // review gate cho kết quả thành công, không phải bắt người dùng tự tạo lại output của task lỗi.
        var harness = new Harness(FailingAgent("worker", "quota_exhausted"), source: "manual");
        harness.Planner
            .ReplanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<IReadOnlyList<OrchestrationPlanTask>>(), Arg.Any<CancellationToken>())
            .Returns(PlanOf(Task("t1", "worker")));

        var result = await harness.RunAsync(PlanOf(Task("t1", "worker")));

        result.Reason.Should().Be("max_rounds");
        await harness.Planner.ReceivedWithAnyArgs(1).ReplanAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task SkippedTask_SatisfiesDependents_SoTheRunCanFinish()
    {
        // "skipped" là trạng thái người dùng đặt khi bỏ qua một bước. Nếu nó không thỏa phụ thuộc thì
        // các bước sau kẹt vĩnh viễn và phiên chết bằng dependency_blocked.
        var harness = new Harness(SucceedingAgent("worker", "ok"));
        var plan = new OrchestrationPlanDocument(1,
        [
            Task("t1", "worker") with { Status = "skipped" },
            Task("t2", "worker", dependsOn: ["t1"]),
        ]);

        var result = await harness.RunAsync(plan);

        result.Status.Should().Be("completed");
        harness.LastPersistedPlan!.Tasks.Single(t => t.Id == "t2").Status.Should().Be("completed");
    }

    [Fact]
    public async Task Preflight_ChargesOnlyRemainingTasks_SoResumeIsNotBlockedByAlreadyDoneWork()
    {
        // Hạn mức còn lại chỉ đủ cho 1 task. Tính cả 9 task đã xong sẽ chặn nhầm ngay khi người dùng
        // vừa can thiệp xong và bấm chạy tiếp.
        var harness = new Harness(SucceedingAgent("worker", "ok"), capUsd: 0.05m);
        var tasks = Enumerable.Range(1, 9)
            .Select(i => Task($"done{i}", "worker") with { Status = "completed", Output = "ok" })
            .Append(Task("t10", "worker"))
            .ToArray();

        var result = await harness.RunAsync(new OrchestrationPlanDocument(1, tasks));

        result.Status.Should().Be("completed");
    }

    private static OrchestrationPlanTask Task(string id, string agent, IReadOnlyList<string>? dependsOn = null) =>
        new(id, agent, $"Task {id}", new Dictionary<string, string>(), dependsOn ?? [], "pending", null, null);

    private static OrchestrationPlanDocument PlanOf(params OrchestrationPlanTask[] tasks) => new(1, tasks);

    private static IAgent FailingAgent(string name, string error) =>
        StubAgent(name, task => new AgentResult(task.Id, false, string.Empty, error));

    private static IAgent SucceedingAgent(string name, string output) =>
        StubAgent(name, task => new AgentResult(task.Id, true, output, null));

    private static IAgent StubAgent(string name, Func<AgentTask, AgentResult> execute)
    {
        var agent = Substitute.For<IAgent>();
        agent.Name.Returns(name);
        agent.ExecuteAsync(Arg.Any<AgentTask>(), Arg.Any<CancellationToken>())
            .Returns(call => System.Threading.Tasks.Task.FromResult(execute(call.Arg<AgentTask>())));
        return agent;
    }

    private sealed class Harness
    {
        public Harness(IAgent agent, string policy = OrchestratorFailurePolicies.Pause, decimal capUsd = 100m, string source = "test")
        {
            _source = source;
            Planner = Substitute.For<IAutonomousPlanner>();
            Sink = Substitute.For<IAutonomousRunSink>();

            var catalog = Substitute.For<IAgentDefinitionCatalog>();
            catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AgentDefinitionCatalogEntry>());

            var tracker = Substitute.For<ILlmCostTracker>();
            tracker.SummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new CostSummary(TenantId, 0m, capUsd, 0f));

            var failurePolicy = Substitute.For<IOrchestrationFailurePolicyResolver>();
            failurePolicy.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(policy);

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

            // Sink thật tăng generation mỗi lần thay kế hoạch. Nếu stub cứ trả 0 thì biến đếm replan
            // không bao giờ chạm MaxRounds và vòng lặp replan chạy vô hạn.
            var generation = 0;
            Sink.GetPlanGenerationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => generation);
            Sink.PersistReplanAndRejectSupersededContentAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<OrchestrationPlanDocument>(),
                    Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(_ => ++generation);

            // Bắt lại plan cuối cùng được ghi xuống — kiểm chứng thứ tự ghi/pause và trạng thái task.
            Sink.PersistPlanAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Do<OrchestrationPlanDocument>(plan => LastPersistedPlan = plan),
                    Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(System.Threading.Tasks.Task.CompletedTask);

            _orchestrator = new AutonomousOrchestrator(
                Planner,
                catalog,
                new AgentRegistry([agent]),
                Substitute.For<IA2AMailbox>(),
                new OrchestratorCostGuard(tracker),
                Substitute.For<ILlmCallScope>(),
                Sink,
                Substitute.For<IRagRetriever>(),
                Substitute.For<IClaudeChatClient>(),
                clock,
                new AutonomousOrchestratorOptions { MaxTransientRetries = 0, TransientBackoffBaseMs = 0 },
                failurePolicyResolver: failurePolicy);
        }

        private readonly AutonomousOrchestrator _orchestrator;

        private readonly string _source;

        public IAutonomousPlanner Planner { get; }

        public IAutonomousRunSink Sink { get; }

        public OrchestrationPlanDocument? LastPersistedPlan { get; private set; }

        public Task<AutonomousRunResult> RunAsync(OrchestrationPlanDocument plan) =>
            _orchestrator.RunExistingPlanAsync(
                new AutonomousRunRequest(TenantId, SessionId, "Mục tiêu thử", _source, false, new HashSet<string>()),
                plan);
    }
}
