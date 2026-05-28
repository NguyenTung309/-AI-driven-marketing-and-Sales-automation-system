namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record CostEntry(
    Guid TenantId,
    string AgentCode,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    DateTimeOffset At);

public sealed record CostSummary(Guid TenantId, decimal MonthToDateUsd, decimal CapUsd, float PercentUsed);

public interface IClaudeCostTracker : ISkill
{
    Task RecordAsync(CostEntry entry, CancellationToken ct);
    Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct);
}

// Source: OpenTelemetry gen_ai.* semantic conventions + local SQLite ledger.
// Hard cap (constitution Art.6) = $200/month/tenant; soft alert at 80%.
internal sealed class SqliteClaudeCostTracker : IClaudeCostTracker
{
    public string Name => "claude-cost-tracker";

    public Task RecordAsync(CostEntry entry, CancellationToken ct) =>
        throw new NotImplementedException("TODO: append-only insert into local cost SQLite + emit OTel gen_ai.cost metric.");

    public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct) =>
        throw new NotImplementedException("TODO: SUM(usd_cost) WHERE tenant + date_trunc('month') + cap from config.");
}
