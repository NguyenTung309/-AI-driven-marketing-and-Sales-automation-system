using Clawbot.AgentService.Services;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.AgentService.Tests.Services;

public sealed class AutonomousRunSinkNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteAsync_PublishesCompletionNotification_ToInitiatingUser()
    {
        // EARS[WHEN a run completes THE SYSTEM SHALL notify the initiating user with the outcome]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var userId = Guid.NewGuid();
        var session = AgentSession.Start(fx.TenantId, agentId: null, conversationId: null, "launch HSK", Now, userId: userId);
        fx.Db.AgentSessions.Add(session);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<INotificationPublisher>();
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(ci.ArgAt<string>(0), Array.Empty<PiiSpan>()));
        var sut = new AutonomousRunSink(fx.Db, pii, new FixedClock(Now), publisher, NullLogger<AutonomousRunSink>.Instance);

        await sut.CompleteAsync(fx.TenantId, session.Id, Now, CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(r => r.TenantId == fx.TenantId && r.UserId == userId && r.Type == "orchestration_completed" && r.Severity == "success"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailAsync_PublishesFailureNotification()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var session = AgentSession.Start(fx.TenantId, agentId: null, conversationId: null, "launch HSK", Now);
        fx.Db.AgentSessions.Add(session);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<INotificationPublisher>();
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(ci.ArgAt<string>(0), Array.Empty<PiiSpan>()));
        var sut = new AutonomousRunSink(fx.Db, pii, new FixedClock(Now), publisher, NullLogger<AutonomousRunSink>.Instance);

        await sut.FailAsync(fx.TenantId, session.Id, "max_rounds", Now, CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(r => r.Type == "orchestration_failed" && r.Severity == "warning" && (r.Body ?? string.Empty).Contains("max_rounds")),
            Arg.Any<CancellationToken>());
        var saved = fx.Db.AgentSessions.IgnoreQueryFilters().First();
        saved.Status.Should().Be(AgentSessionStatuses.Failed);
    }

    [Fact]
    public async Task PersistPlanAsync_WithApproval_PublishesApprovalNotification()
    {
        // EARS[WHEN a plan requires approval THE SYSTEM SHALL notify the initiating user it is awaiting approval]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var session = AgentSession.Start(fx.TenantId, agentId: null, conversationId: null, "launch HSK", Now);
        fx.Db.AgentSessions.Add(session);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<INotificationPublisher>();
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(ci.ArgAt<string>(0), Array.Empty<PiiSpan>()));
        var sut = new AutonomousRunSink(fx.Db, pii, new FixedClock(Now), publisher, NullLogger<AutonomousRunSink>.Instance);
        var plan = new Clawbot.Agents.Core.Orchestrator.OrchestrationPlanDocument(3, Array.Empty<Clawbot.Agents.Core.Orchestrator.OrchestrationPlanTask>());

        await sut.PersistPlanAsync(fx.TenantId, session.Id, plan, requiresApproval: true, CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(r => r.Type == "orchestration_approval"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WithoutPublisher_DoesNotThrow()
    {
        // ponytail: publisher is optional (AgentService tests / older configs); terminal flow must still persist.
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var session = AgentSession.Start(fx.TenantId, agentId: null, conversationId: null, "launch HSK", Now);
        fx.Db.AgentSessions.Add(session);
        await fx.Db.SaveChangesAsync();
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(ci.ArgAt<string>(0), Array.Empty<PiiSpan>()));
        var sut = new AutonomousRunSink(fx.Db, pii, new FixedClock(Now), publisher: null, NullLogger<AutonomousRunSink>.Instance);

        var act = async () => await sut.CompleteAsync(fx.TenantId, session.Id, Now, CancellationToken.None);

        await act.Should().NotThrowAsync();
        fx.Db.AgentSessions.IgnoreQueryFilters().First().Status.Should().Be(AgentSessionStatuses.Completed);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
