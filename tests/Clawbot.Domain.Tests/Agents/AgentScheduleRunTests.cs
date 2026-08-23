using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentScheduleRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ScheduleId = Guid.NewGuid();

    private static AgentScheduleRun CreateStarted() =>
        AgentScheduleRun.Start(TenantId, ScheduleId, "2026-08-17T12:00", Now);

    [Fact]
    public void Start_SetsInitialDefaults()
    {
        var run = CreateStarted();

        run.TenantId.Should().Be(TenantId);
        run.ScheduleId.Should().Be(ScheduleId);
        run.WindowKey.Should().Be("2026-08-17T12:00");
        run.Status.Should().Be("started");
        run.StartedAt.Should().Be(Now);
        run.LastHeartbeatAt.Should().Be(Now);
        run.FinishedAt.Should().BeNull();
        run.SessionId.Should().BeNull();
    }

    [Fact]
    public void LinkSession_SetsSessionId()
    {
        var run = CreateStarted();
        var sessionId = Guid.NewGuid();

        run.LinkSession(sessionId);

        run.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public void Heartbeat_UpdatesLastHeartbeatWhenStarted()
    {
        var run = CreateStarted();

        run.Heartbeat(Now.AddMinutes(5));

        run.LastHeartbeatAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Heartbeat_IgnoresAfterCompletion()
    {
        var run = CreateStarted();
        run.Complete(Now.AddMinutes(10));

        run.Heartbeat(Now.AddMinutes(15));

        run.LastHeartbeatAt.Should().Be(Now); // unchanged from start
    }

    [Fact]
    public void Complete_TransitionsToCompleted()
    {
        var run = CreateStarted();

        run.Complete(Now.AddMinutes(10));

        run.Status.Should().Be("completed");
        run.FinishedAt.Should().Be(Now.AddMinutes(10));
        run.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_TransitionsToFailedWithError()
    {
        var run = CreateStarted();

        run.Fail("agent crashed", Now.AddMinutes(10));

        run.Status.Should().Be("failed");
        run.FinishedAt.Should().Be(Now.AddMinutes(10));
        run.Error.Should().Be("agent crashed");
    }

    [Fact]
    public void Cancel_TransitionsToCancelled()
    {
        var run = CreateStarted();

        run.Cancel(Now.AddMinutes(10));

        run.Status.Should().Be("cancelled");
        run.FinishedAt.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public void SkipOverlap_TransitionsToSkippedOverlap()
    {
        var run = CreateStarted();

        run.SkipOverlap(Now.AddMinutes(1));

        run.Status.Should().Be("skipped_overlap");
        run.FinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void SetInitiator_UpdatesUserId()
    {
        var run = CreateStarted();
        var userId = Guid.NewGuid();

        run.SetInitiator(userId);

        run.InitiatorUserId.Should().Be(userId);
    }

    [Fact]
    public void SetInitiator_ClearsWithNull()
    {
        var run = AgentScheduleRun.Start(TenantId, ScheduleId, "w", Now, Guid.NewGuid());

        run.SetInitiator(null);

        run.InitiatorUserId.Should().BeNull();
    }
}
