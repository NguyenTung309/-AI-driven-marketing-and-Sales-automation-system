using Clawbot.AgentService.Services;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Research;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Agents;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class AgentScheduleRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunDueAsync_CreatesSessionAndCompletesRun()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var schedule = AgentSchedule.Create(fx.TenantId, "daily", "Say hi", "daily", null, "UTC", Now, false, Now);
        fx.Db.AgentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var run = await sut.RunDueAsync(schedule.Id, Now, CancellationToken.None);

        run.Should().NotBeNull();
        run!.Status.Should().Be("completed");
        run.SessionId.Should().NotBeNull();
        (await fx.Db.AgentSessions.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await fx.Db.AgentSchedules.IgnoreQueryFilters().SingleAsync()).NextRunAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public async Task RunDueAsync_SkipsOverlap_WhenPreviousRunStillStarted()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var schedule = AgentSchedule.Create(fx.TenantId, "daily", "Say hi", "daily", null, "UTC", Now, false, Now);
        fx.Db.AgentSchedules.Add(schedule);
        fx.Db.AgentScheduleRuns.Add(AgentScheduleRun.Start(fx.TenantId, schedule.Id, "daily:2026-06-23", Now.AddDays(-1)));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var run = await sut.RunDueAsync(schedule.Id, Now, CancellationToken.None);

        run.Should().NotBeNull();
        run!.Status.Should().Be("skipped_overlap");
        run.SessionId.Should().BeNull();
        (await fx.Db.AgentSessions.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RunDueAsync_ReturnsExistingRun_ForDuplicateWindow()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var schedule = AgentSchedule.Create(fx.TenantId, "daily", "Say hi", "daily", null, "UTC", Now, false, Now);
        var existing = AgentScheduleRun.Start(fx.TenantId, schedule.Id, "daily:2026-06-24", Now);
        existing.Complete(Now);
        fx.Db.AgentSchedules.Add(schedule);
        fx.Db.AgentScheduleRuns.Add(existing);
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var run = await sut.RunDueAsync(schedule.Id, Now, CancellationToken.None);

        run.Should().BeSameAs(existing);
        (await fx.Db.AgentScheduleRuns.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunDueAsync_TrendScanMarker_RunsScannerWithoutSession()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var schedule = AgentSchedule.Create(fx.TenantId, "Quét xu hướng", "[trend-scan]", "daily", null, "UTC", Now, false, Now);
        fx.Db.AgentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var scanner = Substitute.For<ITenantTrendScanner>();
        scanner.ScanAndPersistAsync(fx.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScoredTrend> { new("hsk", "google_trends", "20K+", 10d, []) });
        var sut = Build(fx, scanner);

        var run = await sut.RunDueAsync(schedule.Id, Now, CancellationToken.None);

        run.Should().NotBeNull();
        run!.Status.Should().Be("completed");
        run.SessionId.Should().BeNull();
        (await fx.Db.AgentSessions.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        await scanner.Received(1).ScanAndPersistAsync(fx.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await fx.Db.AgentSchedules.IgnoreQueryFilters().SingleAsync()).NextRunAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public async Task RunDueAsync_EventTriggeredSchedule_UsesTicksWindowAndSleepsUntilNextEvent()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var schedule = AgentSchedule.Create(
            fx.TenantId, "Quét xu hướng theo sự kiện", "[trend-scan]", "daily", null, "UTC", Now, false, Now,
            triggerType: "event", eventKey: "content.trends.scanned");
        fx.Db.AgentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var scanner = Substitute.For<ITenantTrendScanner>();
        scanner.ScanAndPersistAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ScoredTrend>());
        var sut = Build(fx, scanner);

        var run = await sut.RunDueAsync(schedule.Id, Now, CancellationToken.None);

        run.Should().NotBeNull();
        run!.Status.Should().Be("completed");
        run.WindowKey.Should().Be($"event:{Now.UtcTicks}");
        // Event schedules ngủ tới khi dispatcher kéo NextRunAt về — không lặp theo cadence.
        (await fx.Db.AgentSchedules.IgnoreQueryFilters().SingleAsync()).NextRunAt.Should().Be(DateTimeOffset.MaxValue);
    }

    [Fact]
    public async Task RunDueAsync_TrendScanFailure_MarksRunFailed()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var schedule = AgentSchedule.Create(fx.TenantId, "Quét xu hướng", "[trend-scan]", "daily", null, "UTC", Now, false, Now);
        fx.Db.AgentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var scanner = Substitute.For<ITenantTrendScanner>();
        scanner.ScanAndPersistAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ScoredTrend>>(_ => throw new InvalidOperationException("boom"));
        var sut = Build(fx, scanner);

        var run = await sut.RunDueAsync(schedule.Id, Now, CancellationToken.None);

        run.Should().NotBeNull();
        run!.Status.Should().Be("failed");
    }

    private static AgentScheduleRunner Build(AgentServiceTestAppDb fx, ITenantTrendScanner? trendScanner = null)
    {
        var planner = Substitute.For<IAutonomousPlanner>();
        planner.PlanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<AgentCatalogEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new OrchestrationPlanDocument(3, new[]
            {
                new OrchestrationPlanTask("t1", "content-agent", "Do", new Dictionary<string, string>(), Array.Empty<string>(), "pending", null, null),
            }));
        var catalog = Substitute.For<IAgentDefinitionCatalog>();
        catalog.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new AgentDefinitionCatalogEntry(Guid.NewGuid(), "content-agent", "content", "Content", "content", "Do", "{}", true, null) });
        var mailbox = Substitute.For<IA2AMailbox>();
        var agent = Substitute.For<IAgent>();
        agent.Name.Returns("content-agent");
        agent.ExecuteAsync(Arg.Any<AgentTask>(), Arg.Any<CancellationToken>())
            .Returns(new AgentResult("t1", true, "ok", null));
        var tracker = Substitute.For<ILlmCostTracker>();
        tracker.SummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CostSummary(fx.TenantId, 0m, 100m, 0));
        var clock = new FixedClock(Now);
        var rag = Substitute.For<IRagRetriever>();
        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("ok", 0, 0, 0m));
        var orchestrator = new AutonomousOrchestrator(
            planner,
            catalog,
            new AgentRegistry(new[] { agent }),
            mailbox,
            new OrchestratorCostGuard(tracker),
            new LlmCallScope(),
            new AutonomousRunSink(fx.Db, new RegexPiiRedactor(), clock),
            rag,
            chat,
            clock);
        return new AgentScheduleRunner(fx.Db, orchestrator, trendScanner ?? Substitute.For<ITenantTrendScanner>(), clock);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
