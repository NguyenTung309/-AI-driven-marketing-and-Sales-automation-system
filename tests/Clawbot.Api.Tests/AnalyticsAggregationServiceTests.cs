using Clawbot.Api.Services;
using Clawbot.Domain.Agents;
using Clawbot.SharedKernel.Time;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class AnalyticsAggregationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAgentPerformanceAsync_reports_quality_samples_by_agent_trace()
    {
        using var fx = new TestApiAppDb(TenantId);
        var session = AgentSession.Start(TenantId, agentId: null, conversationId: null, "chat quality eval", Now);
        session.AppendTrace("q-1", "chat", "quality", """{"passed":true,"score":0.9}""", Now.AddMinutes(1));
        session.AppendTrace("q-2", "chat", "quality", """{"passed":false,"score":0.4}""", Now.AddMinutes(2));
        session.AppendTrace("run", "chat", "reply", "normal trace", Now.AddMinutes(3));
        session.Finish(Now.AddMinutes(4));
        fx.Db.AgentSessions.Add(session);
        await fx.Db.SaveChangesAsync();

        var sut = new AnalyticsAggregationService(fx.Db, new FixedClock(Now));

        var result = await sut.GetAgentPerformanceAsync(
            TenantId,
            DateOnly.FromDateTime(Now.DateTime),
            DateOnly.FromDateTime(Now.DateTime));

        var item = result.Should().ContainSingle().Subject;
        item.AgentName.Should().Be("chat");
        item.Sessions.Should().Be(1);
        item.CompletedSessions.Should().Be(1);
        item.TraceCount.Should().Be(3);
        item.QualitySamples.Should().Be(2);
        item.PassedQualitySamples.Should().Be(1);
        item.QualityPassRate.Should().Be(0.5m);
        item.AverageQualityScore.Should().Be(0.65m);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
