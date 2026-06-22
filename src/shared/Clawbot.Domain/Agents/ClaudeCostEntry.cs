using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

/// <summary>One persisted LLM-call cost row (claude_cost_ledger). Feeds the agent-cost report.</summary>
public sealed class ClaudeCostEntry : Entity<Guid>, ITenantOwned
{
    public const string ReservationAgentCode = "__cost_reservation__";
    public const string ReservationModel = "reserved-budget";

    public Guid TenantId { get; private set; }
    public string AgentCode { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal Usd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ClaudeCostEntry() { }

    public static ClaudeCostEntry Create(
        Guid tenantId,
        string agentCode,
        string model,
        int inputTokens,
        int outputTokens,
        decimal usd,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentCode = agentCode,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Usd = usd,
            CreatedAt = createdAt,
        };

    public static ClaudeCostEntry CreateReservation(Guid tenantId, Guid reservationId, decimal usd, DateTimeOffset createdAt) =>
        new()
        {
            Id = reservationId,
            TenantId = tenantId,
            AgentCode = ReservationAgentCode,
            Model = ReservationModel,
            InputTokens = 0,
            OutputTokens = 0,
            Usd = Math.Max(0m, usd),
            CreatedAt = createdAt,
        };

    public void ReleaseReservation()
    {
        if (!string.Equals(AgentCode, ReservationAgentCode, StringComparison.Ordinal))
            throw new InvalidOperationException("Only cost reservation rows can be released.");

        Usd = 0m;
    }

    public void ApplyReservation(string agentCode, string model, int inputTokens, int outputTokens, decimal usd)
    {
        if (!string.Equals(AgentCode, ReservationAgentCode, StringComparison.Ordinal))
            throw new InvalidOperationException("Only cost reservation rows can be applied.");

        AgentCode = agentCode;
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Usd = Math.Max(0m, usd);
    }
}
