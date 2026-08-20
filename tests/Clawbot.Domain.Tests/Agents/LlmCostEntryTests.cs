using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class LlmCostEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var sessionId = Guid.NewGuid();
        var entry = LlmCostEntry.Create(TenantId, "chat-agent", "gpt-4o", 1000, 500, 0.03m, Now, sessionId, isEstimated: true);

        entry.TenantId.Should().Be(TenantId);
        entry.AgentCode.Should().Be("chat-agent");
        entry.Model.Should().Be("gpt-4o");
        entry.InputTokens.Should().Be(1000);
        entry.OutputTokens.Should().Be(500);
        entry.Usd.Should().Be(0.03m);
        entry.SessionId.Should().Be(sessionId);
        entry.IsEstimated.Should().BeTrue();
        entry.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void CreateReservation_UsesReservedAgentCodeAndModel()
    {
        var reservationId = Guid.NewGuid();
        var entry = LlmCostEntry.CreateReservation(TenantId, reservationId, 5.00m, Now);

        entry.Id.Should().Be(reservationId);
        entry.AgentCode.Should().Be(LlmCostEntry.ReservationAgentCode);
        entry.Model.Should().Be(LlmCostEntry.ReservationModel);
        entry.InputTokens.Should().Be(0);
        entry.OutputTokens.Should().Be(0);
        entry.Usd.Should().Be(5.00m);
    }

    [Fact]
    public void CreateReservation_ClampsNegativeUsdToZero()
    {
        var entry = LlmCostEntry.CreateReservation(TenantId, Guid.NewGuid(), -10m, Now);

        entry.Usd.Should().Be(0m);
    }

    [Fact]
    public void ReleaseReservation_ZeroesUsdOnReservationRow()
    {
        var entry = LlmCostEntry.CreateReservation(TenantId, Guid.NewGuid(), 5m, Now);

        entry.ReleaseReservation();

        entry.Usd.Should().Be(0m);
    }

    [Fact]
    public void ReleaseReservation_ThrowsOnNonReservationRow()
    {
        var entry = LlmCostEntry.Create(TenantId, "agent", "model", 10, 5, 0.01m, Now);

        var act = () => entry.ReleaseReservation();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyReservation_ReplacesWithActualCostData()
    {
        var entry = LlmCostEntry.CreateReservation(TenantId, Guid.NewGuid(), 5m, Now);
        var sessionId = Guid.NewGuid();

        entry.ApplyReservation("real-agent", "claude-3", 2000, 800, 0.05m, sessionId, isEstimated: false);

        entry.AgentCode.Should().Be("real-agent");
        entry.Model.Should().Be("claude-3");
        entry.InputTokens.Should().Be(2000);
        entry.OutputTokens.Should().Be(800);
        entry.Usd.Should().Be(0.05m);
        entry.SessionId.Should().Be(sessionId);
        entry.IsEstimated.Should().BeFalse();
    }

    [Fact]
    public void ApplyReservation_ClampsNegativeUsd()
    {
        var entry = LlmCostEntry.CreateReservation(TenantId, Guid.NewGuid(), 5m, Now);

        entry.ApplyReservation("a", "m", 0, 0, -1m);

        entry.Usd.Should().Be(0m);
    }

    [Fact]
    public void ApplyReservation_ThrowsOnNonReservationRow()
    {
        var entry = LlmCostEntry.Create(TenantId, "agent", "model", 10, 5, 0.01m, Now);

        var act = () => entry.ApplyReservation("a", "m", 0, 0, 0m);

        act.Should().Throw<InvalidOperationException>();
    }
}
