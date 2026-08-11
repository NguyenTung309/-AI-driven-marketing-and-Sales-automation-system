using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AgentSessionLifecycleTests
{
    [Fact]
    public void Finish_RejectsTerminalSession()
    {
        var session = RunningSession();
        session.Fail(DateTimeOffset.UtcNow);

        var action = () => session.Finish(DateTimeOffset.UtcNow);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Only a running orchestration session can complete.");
    }

    [Fact]
    public void Fail_RejectsTerminalSession()
    {
        var session = RunningSession();
        session.Finish(DateTimeOffset.UtcNow);

        var action = () => session.Fail(DateTimeOffset.UtcNow);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Only active orchestration sessions can fail.");
    }

    [Fact]
    public void Fail_AllowsPauseRequestedSession()
    {
        var session = RunningSession();
        session.RequestPause();

        session.Fail(DateTimeOffset.UtcNow);

        session.Status.Should().Be(AgentSessionStatuses.Failed);
    }

    [Fact]
    public void Fail_AllowsPendingApprovalSession_WhenInitiatorIsNoLongerAuthorized()
    {
        var session = AgentSession.CreatePlan(
            Guid.NewGuid(),
            "Create content",
            "{}",
            requiresApproval: true,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        session.Fail(DateTimeOffset.UtcNow);

        session.Status.Should().Be(AgentSessionStatuses.Failed);
    }

    [Fact]
    public void ApplyReplan_RejectsStaleGeneration()
    {
        var session = RunningSession();
        session.ApplyReplan("{\"tasks\":[]}", expectedGeneration: 0);

        var action = () => session.ApplyReplan("{\"tasks\":[]}", expectedGeneration: 0);

        action.Should().Throw<OrchestrationPlanGenerationMismatchException>();
        session.ReplanCount.Should().Be(1);
    }

    [Fact]
    public void DeferCancellation_PersistsTheCurrentGenerationUntilPublicationSettles()
    {
        var at = DateTimeOffset.UtcNow;
        var session = RunningSession();

        session.DeferCancellation(expectedGeneration: 0, at);

        session.Status.Should().Be(AgentSessionStatuses.Cancelling);
        session.PendingTerminalGeneration.Should().Be(0);
        session.PendingTerminalRequestedAt.Should().Be(at);
        session.PendingTerminalReason.Should().BeNull();
    }

    [Fact]
    public void DeferFailure_PersistsReasonAndPreventsReplan()
    {
        var at = DateTimeOffset.UtcNow;
        var session = RunningSession();

        session.DeferFailure("orchestrator_run_failed", expectedGeneration: 0, at);

        session.Status.Should().Be(AgentSessionStatuses.Failing);
        session.PendingTerminalReason.Should().Be("orchestrator_run_failed");
        var action = () => session.ApplyReplan("{\"tasks\":[]}", expectedGeneration: 0);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DeferFailure_TruncatesReasonToDatabaseColumnLimit()
    {
        var session = RunningSession();

        session.DeferFailure(new string('x', AgentSession.MaxPendingTerminalReasonLength + 1), 0, DateTimeOffset.UtcNow);

        session.PendingTerminalReason.Should().HaveLength(AgentSession.MaxPendingTerminalReasonLength);
    }

    [Fact]
    public void DeferCancellation_RejectsStaleGeneration()
    {
        var session = RunningSession();
        session.ApplyReplan("{\"tasks\":[]}", expectedGeneration: 0);

        var action = () => session.DeferCancellation(
            expectedGeneration: 0,
            DateTimeOffset.UtcNow);

        action.Should().Throw<OrchestrationPlanGenerationMismatchException>();
        session.Status.Should().Be(AgentSessionStatuses.Running);
        session.PendingTerminalGeneration.Should().BeNull();
    }

    [Fact]
    public void FinalizeDeferredTerminal_ClearsIntentAndSetsCancelled()
    {
        var at = DateTimeOffset.UtcNow;
        var session = RunningSession();
        session.DeferCancellation(expectedGeneration: 0, at);

        session.FinalizeDeferredTerminal(at.AddMinutes(1));

        session.Status.Should().Be(AgentSessionStatuses.Cancelled);
        session.FinishedAt.Should().Be(at.AddMinutes(1));
        session.PendingTerminalGeneration.Should().BeNull();
        session.PendingTerminalRequestedAt.Should().BeNull();
        session.PendingTerminalReason.Should().BeNull();
    }

    [Fact]
    public void FinalizeDeferredTerminal_ClearsIntentAndSetsFailed()
    {
        var at = DateTimeOffset.UtcNow;
        var session = RunningSession();
        session.DeferFailure("orchestrator_run_failed", expectedGeneration: 0, at);

        session.FinalizeDeferredTerminal(at.AddMinutes(1));

        session.Status.Should().Be(AgentSessionStatuses.Failed);
        session.FinishedAt.Should().Be(at.AddMinutes(1));
        session.PendingTerminalGeneration.Should().BeNull();
        session.PendingTerminalRequestedAt.Should().BeNull();
        session.PendingTerminalReason.Should().BeNull();
    }

    [Fact]
    public void UpdatePlan_RebindsExecutionPrincipalWithoutChangingCreator()
    {
        var creatorUserId = Guid.NewGuid();
        var editorUserId = Guid.NewGuid();
        var session = AgentSession.CreatePlan(
            Guid.NewGuid(),
            "Create content",
            "{}",
            requiresApproval: true,
            DateTimeOffset.UtcNow,
            creatorUserId);

        session.UpdatePlan("{\"tasks\":[]}", editorUserId);

        session.UserId.Should().Be(creatorUserId);
        session.ExecutionUserId.Should().Be(editorUserId);
    }

    [Fact]
    public void Approve_RebindsExecutionPrincipalWithoutChangingCreator()
    {
        var creatorUserId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        var session = AgentSession.CreatePlan(
            Guid.NewGuid(),
            "Create content",
            "{}",
            requiresApproval: true,
            DateTimeOffset.UtcNow,
            creatorUserId);

        session.Approve(approverUserId);

        session.UserId.Should().Be(creatorUserId);
        session.ExecutionUserId.Should().Be(approverUserId);
        session.Status.Should().Be(AgentSessionStatuses.Running);
    }

    private static AgentSession RunningSession() =>
        AgentSession.Start(Guid.NewGuid(), null, null, "Create content", DateTimeOffset.UtcNow);
}
