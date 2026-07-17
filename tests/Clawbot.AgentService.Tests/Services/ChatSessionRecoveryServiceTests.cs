using Clawbot.AgentService.Services;
using Clawbot.Domain.Agents;
using Clawbot.SharedKernel.Time;
using FluentAssertions;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ChatSessionRecoveryServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecoverAsync_fails_only_stale_running_chat_reply_sessions()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var staleChat = AgentSession.Start(TenantId, null, null, "chat-reply", Now.AddMinutes(-6));
        var recentChat = AgentSession.Start(TenantId, null, null, "chat-reply", Now.AddMinutes(-2));
        var staleOrchestration = AgentSession.Start(TenantId, null, null, "orchestration", Now.AddHours(-1));
        fx.Db.AgentSessions.AddRange(staleChat, recentChat, staleOrchestration);
        await fx.Db.SaveChangesAsync();

        var recovered = await ChatSessionRecoveryService.RecoverAsync(fx.Db, new FixedClock(Now));

        recovered.Should().Be(1);
        staleChat.Status.Should().Be(AgentSessionStatuses.Failed);
        staleChat.FinishedAt.Should().Be(Now);
        staleChat.Traces.Should().ContainSingle(t => t.Phase == "recovered_timeout");
        recentChat.Status.Should().Be(AgentSessionStatuses.Running);
        staleOrchestration.Status.Should().Be(AgentSessionStatuses.Running);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
