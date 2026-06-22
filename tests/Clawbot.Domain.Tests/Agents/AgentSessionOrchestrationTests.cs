using Clawbot.Domain.Agents;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentSessionOrchestrationTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreatePlan_sets_pending_approval_when_tenant_requires_approval()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{\"tasks\":[]}", requiresApproval: true, Now);

        session.TenantId.Should().Be(TenantId);
        session.Goal.Should().Be("launch campaign");
        session.PlanJson.Should().Be("{\"tasks\":[]}");
        session.RequiresApproval.Should().BeTrue();
        session.Status.Should().Be(AgentSessionStatuses.PendingApproval);
        session.ReplanCount.Should().Be(0);
        session.StartedAt.Should().Be(Now);
        session.FinishedAt.Should().BeNull();
    }

    [Fact]
    public void CreatePlan_auto_runs_when_approval_is_not_required()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{\"tasks\":[]}", requiresApproval: false, Now);

        session.RequiresApproval.Should().BeFalse();
        session.Status.Should().Be(AgentSessionStatuses.Running);
    }

    [Fact]
    public void Approve_moves_pending_plan_to_running()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{}", requiresApproval: true, Now);

        session.Approve();

        session.Status.Should().Be(AgentSessionStatuses.Running);
    }

    [Fact]
    public void Approve_rejects_non_pending_plan()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{}", requiresApproval: false, Now);

        var act = () => session.Approve();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Only pending orchestration plans can be approved.");
    }

    [Fact]
    public void Pause_and_resume_only_allow_valid_running_states()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{}", requiresApproval: false, Now);

        session.Pause();
        session.Status.Should().Be(AgentSessionStatuses.Paused);

        session.Resume();
        session.Status.Should().Be(AgentSessionStatuses.Running);
    }

    [Fact]
    public void IncrementReplan_increases_replan_counter()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{}", requiresApproval: false, Now);

        session.IncrementReplan();
        session.IncrementReplan();

        session.ReplanCount.Should().Be(2);
    }

    [Fact]
    public void UpdatePlan_replaces_plan_json_while_pending_approval()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{}", requiresApproval: true, Now);

        session.UpdatePlan("{\"version\":1,\"tasks\":[]}");

        session.PlanJson.Should().Be("{\"version\":1,\"tasks\":[]}");
        session.Status.Should().Be(AgentSessionStatuses.PendingApproval);
    }

    [Fact]
    public void UpdatePlan_rejects_edits_once_running()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{}", requiresApproval: false, Now);

        var act = () => session.UpdatePlan("{\"version\":1,\"tasks\":[]}");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Only draft or pending orchestration plans can be edited.");
    }

    [Fact]
    public void Cancel_marks_session_finished()
    {
        var session = AgentSession.CreatePlan(TenantId, "launch campaign", "{}", requiresApproval: false, Now);
        var finishedAt = Now.AddMinutes(5);

        session.Cancel(finishedAt);

        session.Status.Should().Be(AgentSessionStatuses.Cancelled);
        session.FinishedAt.Should().Be(finishedAt);
    }
}
