using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

/// <summary>One persisted LLM-call cost row (claude_cost_ledger). Feeds the agent-cost report.</summary>
public sealed class ClaudeCostEntry : Entity<Guid>, ITenantOwned
{
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
}
