using Clawbot.Domain.Agents;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentOrchestrationPhase1Tests
{
    [Fact]
    public void AgentDefinition_UpdateDefinition_IncrementsVersion()
    {
        var at = new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);
        var agent = AgentDefinition.Create(Guid.NewGuid(), "reviewer-agent", "Reviewer", "reviewer", "Review output", at);

        agent.UpdateDefinition("Reviewer v2", "reviewer", "Review output carefully", "[]", "{}", "{}", "session", null, true, at.AddMinutes(1));

        agent.DisplayName.Should().Be("Reviewer v2");
        agent.Version.Should().Be(2);
        agent.UpdatedAt.Should().Be(at.AddMinutes(1));
    }

    [Fact]
    public void AgentA2AMessage_ClaimThenComplete_RecordsProcessingLifecycle()
    {
        var at = new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);
        var message = AgentA2AMessage.Send(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "task-1",
            "delegate",
            "{}",
            at);

        message.Claim(at.AddSeconds(1));
        message.Complete("{\"ok\":true}", at.AddSeconds(2));

        message.Status.Should().Be("completed");
        message.PayloadJson.Should().Contain("ok");
        message.ProcessedAt.Should().Be(at.AddSeconds(2));
    }

    [Fact]
    public void AgentA2AMessage_CompleteBeforeClaim_Throws()
    {
        var message = AgentA2AMessage.Send(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "task-1", "delegate", "{}", DateTimeOffset.UtcNow);

        var act = () => message.Complete("{}", DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>().WithMessage("Only processing A2A messages can be completed.");
    }

    [Fact]
    public void AgentSchedule_RecordRun_UpdatesLastAndNextRun()
    {
        var at = new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);
        var schedule = AgentSchedule.Create(
            Guid.NewGuid(),
            "Daily lead triage",
            "Review hot leads",
            "daily",
            null,
            "Asia/Ho_Chi_Minh",
            at,
            requiresApproval: false,
            createdAt: at);

        schedule.RecordRun(at, at.AddDays(1), at.AddMinutes(1));

        schedule.LastRunAt.Should().Be(at);
        schedule.NextRunAt.Should().Be(at.AddDays(1));
        schedule.UpdatedAt.Should().Be(at.AddMinutes(1));
    }

    [Fact]
    public void AgentScheduleRun_SkipOverlap_MarksSkippedOverlap()
    {
        var run = AgentScheduleRun.Start(Guid.NewGuid(), Guid.NewGuid(), "daily:2026-06-24", DateTimeOffset.UtcNow);

        run.SkipOverlap(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));

        run.Status.Should().Be("skipped_overlap");
        run.FinishedAt.Should().NotBeNull();
    }
}
