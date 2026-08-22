using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUser = Guid.NewGuid();

    private static AgentSession CreateRunning() =>
        AgentSession.Start(TenantId, null, null, "test goal", Now, UserId);

    [Fact]
    public void Start_SetsInitialDefaults()
    {
        var session = CreateRunning();

        session.TenantId.Should().Be(TenantId);
        session.Status.Should().Be(AgentSessionStatuses.Running);
        session.Goal.Should().Be("test goal");
        session.UserId.Should().Be(UserId);
        session.ExecutionUserId.Should().Be(UserId);
        session.ReplanCount.Should().Be(0);
        session.PlanJson.Should().Be("{}");
        session.FinishedAt.Should().BeNull();
        session.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void CreatePlan_SetsPendingApprovalWhenRequired()
    {
        var session = AgentSession.CreatePlan(
            TenantId, "goal", "{\"steps\":[]}", requiresApproval: true, Now, UserId);

        session.Status.Should().Be(AgentSessionStatuses.PendingApproval);
        session.PlanJson.Should().Be("{\"steps\":[]}");
        session.RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public void CreatePlan_SetsRunningWhenNoApprovalRequired()
    {
        var session = AgentSession.CreatePlan(
            TenantId, "goal", "{}", requiresApproval: false, Now);

        session.Status.Should().Be(AgentSessionStatuses.Running);
    }

    [Fact]
    public void CreatePlan_NormalizesEmptyPlanToJsonObject()
    {
        var session = AgentSession.CreatePlan(TenantId, "goal", "", false, Now);

        session.PlanJson.Should().Be("{}");
    }

    [Fact]
    public void Approve_TransitionsFromPendingApproval()
    {
        var session = AgentSession.CreatePlan(TenantId, "goal", "{}", true, Now, UserId);

        session.Approve(OtherUser);

        session.Status.Should().Be(AgentSessionStatuses.Running);
        session.ExecutionUserId.Should().Be(OtherUser);
    }

    [Fact]
    public void Approve_ThrowsWhenNotPendingApproval()
    {
        var session = CreateRunning();

        var act = () => session.Approve(UserId);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdatePlan_WorksInDraftPendingOrPaused()
    {
        var session = AgentSession.CreatePlan(TenantId, "goal", "{}", true, Now, UserId);

        session.UpdatePlan("{\"v\":2}", OtherUser);

        session.PlanJson.Should().Be("{\"v\":2}");
        session.ExecutionUserId.Should().Be(OtherUser);
    }

    [Fact]
    public void UpdatePlan_ThrowsWhenRunning()
    {
        var session = CreateRunning();

        var act = () => session.UpdatePlan("{\"v\":2}", UserId);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequestPause_TransitionsFromRunning()
    {
        var session = CreateRunning();

        session.RequestPause();

        session.Status.Should().Be(AgentSessionStatuses.PauseRequested);
    }

    [Fact]
    public void RequestPause_ThrowsWhenNotRunning()
    {
        var session = AgentSession.CreatePlan(TenantId, "goal", "{}", true, Now);

        var act = () => session.RequestPause();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AcknowledgePause_TransitionsFromPauseRequested()
    {
        var session = CreateRunning();
        session.RequestPause();

        session.AcknowledgePause();

        session.Status.Should().Be(AgentSessionStatuses.Paused);
    }

    [Fact]
    public void Resume_TransitionsFromPaused()
    {
        var session = CreateRunning();
        session.RequestPause();
        session.AcknowledgePause();

        session.Resume(OtherUser);

        session.Status.Should().Be(AgentSessionStatuses.Running);
        session.ExecutionUserId.Should().Be(OtherUser);
    }

    [Fact]
    public void PauseForIntervention_TransitionsFromRunning()
    {
        var session = CreateRunning();

        session.PauseForIntervention();

        session.Status.Should().Be(AgentSessionStatuses.Paused);
    }

    [Fact]
    public void PauseForIntervention_TransitionsFromPauseRequested()
    {
        var session = CreateRunning();
        session.RequestPause();

        session.PauseForIntervention();

        session.Status.Should().Be(AgentSessionStatuses.Paused);
    }

    [Fact]
    public void PauseForIntervention_ThrowsWhenPaused()
    {
        var session = CreateRunning();
        session.RequestPause();
        session.AcknowledgePause();

        var act = () => session.PauseForIntervention();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyGeneratedPlan_SetsPlanAndOptionallyRequiresApproval()
    {
        var session = CreateRunning();

        session.ApplyGeneratedPlan("{\"tasks\":[1]}", requiresApproval: true);

        session.PlanJson.Should().Be("{\"tasks\":[1]}");
        session.RequiresApproval.Should().BeTrue();
        session.Status.Should().Be(AgentSessionStatuses.PendingApproval);
    }

    [Fact]
    public void ApplyGeneratedPlan_StaysRunningWhenNoApproval()
    {
        var session = CreateRunning();

        session.ApplyGeneratedPlan("{\"tasks\":[1]}", requiresApproval: false);

        session.Status.Should().Be(AgentSessionStatuses.Running);
    }

    [Fact]
    public void ApplyGeneratedPlan_ThrowsWhenNotRunning()
    {
        var session = AgentSession.CreatePlan(TenantId, "goal", "{}", true, Now);

        var act = () => session.ApplyGeneratedPlan("{}", false);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyReplan_IncrementsGeneration()
    {
        var session = CreateRunning();

        var gen = session.ApplyReplan("{\"replanned\":true}", expectedGeneration: 0);

        gen.Should().Be(1);
        session.ReplanCount.Should().Be(1);
        session.PlanJson.Should().Be("{\"replanned\":true}");
    }

    [Fact]
    public void ApplyReplan_ThrowsOnGenerationMismatch()
    {
        var session = CreateRunning();

        var act = () => session.ApplyReplan("{}", expectedGeneration: 5);

        act.Should().Throw<OrchestrationPlanGenerationMismatchException>();
    }

    [Fact]
    public void ApplyReplan_ThrowsWhenNotRunning()
    {
        var session = CreateRunning();
        session.Finish(Now);

        var act = () => session.ApplyReplan("{}", 0);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_TransitionsToCancelled()
    {
        var session = CreateRunning();

        session.Cancel(Now.AddMinutes(1));

        session.Status.Should().Be(AgentSessionStatuses.Cancelled);
        session.FinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Cancel_ThrowsWhenAlreadyCompleted()
    {
        var session = CreateRunning();
        session.Finish(Now);

        var act = () => session.Cancel(Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DeferCancellation_SetsCancellingState()
    {
        var session = CreateRunning();

        session.DeferCancellation(expectedGeneration: 0, Now);

        session.Status.Should().Be(AgentSessionStatuses.Cancelling);
        session.PendingTerminalGeneration.Should().Be(0);
        session.PendingTerminalRequestedAt.Should().Be(Now);
    }

    [Fact]
    public void DeferFailure_SetsFailingStateWithReason()
    {
        var session = CreateRunning();

        session.DeferFailure("task failed", expectedGeneration: 0, Now);

        session.Status.Should().Be(AgentSessionStatuses.Failing);
        session.PendingTerminalReason.Should().Be("task failed");
    }

    [Fact]
    public void FinalizeDeferredTerminal_CompletesCancellation()
    {
        var session = CreateRunning();
        session.DeferCancellation(0, Now);

        session.FinalizeDeferredTerminal(Now.AddMinutes(1));

        session.Status.Should().Be(AgentSessionStatuses.Cancelled);
        session.FinishedAt.Should().Be(Now.AddMinutes(1));
        session.PendingTerminalGeneration.Should().BeNull();
    }

    [Fact]
    public void FinalizeDeferredTerminal_CompletesFailure()
    {
        var session = CreateRunning();
        session.DeferFailure("error", 0, Now);

        session.FinalizeDeferredTerminal(Now.AddMinutes(1));

        session.Status.Should().Be(AgentSessionStatuses.Failed);
        session.FinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void FinalizeDeferredTerminal_ThrowsWhenNoPendingIntent()
    {
        var session = CreateRunning();

        var act = () => session.FinalizeDeferredTerminal(Now);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*orchestration_terminal_intent_missing*");
    }

    [Fact]
    public void Finish_TransitionsFromRunning()
    {
        var session = CreateRunning();

        session.Finish(Now.AddMinutes(5));

        session.Status.Should().Be(AgentSessionStatuses.Completed);
        session.FinishedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Fail_TransitionsFromActiveStates()
    {
        var session = CreateRunning();

        session.Fail(Now.AddMinutes(5));

        session.Status.Should().Be(AgentSessionStatuses.Failed);
        session.FinishedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Fail_ThrowsWhenAlreadyCompleted()
    {
        var session = CreateRunning();
        session.Finish(Now);

        var act = () => session.Fail(Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Archive_WorksFromTerminalStates()
    {
        var session = CreateRunning();
        session.Finish(Now);

        session.Archive(Now.AddMinutes(1));

        session.ArchivedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Archive_ThrowsWhenRunning()
    {
        var session = CreateRunning();

        var act = () => session.Archive(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Unarchive_ClearsArchivedAt()
    {
        var session = CreateRunning();
        session.Finish(Now);
        session.Archive(Now.AddMinutes(1));

        session.Unarchive();

        session.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void AppendTrace_AddsToCollection()
    {
        var session = CreateRunning();

        var trace = session.AppendTrace("task-1", "writer", "generate", "started", Now);

        session.Traces.Should().ContainSingle();
        trace.TaskId.Should().Be("task-1");
    }

    [Fact]
    public void SetExecutionPrincipal_ThrowsOnEmptyGuid()
    {
        var session = CreateRunning();

        var act = () => session.SetExecutionPrincipal(Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*execution_user_id_required*");
    }

    [Fact]
    public void RecordRun_UpdatesPlanJson()
    {
        var session = CreateRunning();

        session.RecordRun("{\"executed\":true}");

        session.PlanJson.Should().Be("{\"executed\":true}");
    }

    [Fact]
    public void DeferFailure_TruncatesLongReason()
    {
        var session = CreateRunning();
        var longReason = new string('x', AgentSession.MaxPendingTerminalReasonLength + 100);

        session.DeferFailure(longReason, 0, Now);

        session.PendingTerminalReason!.Length.Should().Be(AgentSession.MaxPendingTerminalReasonLength);
    }
}
