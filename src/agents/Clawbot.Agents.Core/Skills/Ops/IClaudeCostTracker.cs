using System.Collections.Concurrent;

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

// Baseline in-memory tracker keyed by (tenant, year-month).
// Constitution Art.6: $200/month/tenant hard cap, soft alert 80%.
// Vendor swap target: SQLite ledger + OTel gen_ai.cost metric emission.
internal sealed class InMemoryClaudeCostTracker : IClaudeCostTracker
{
    private const decimal DefaultCapUsd = 200m;

    private readonly ConcurrentDictionary<(Guid Tenant, int Year, int Month), decimal> _ledger = new();

    public string Name => "claude-cost-tracker";

    public Task RecordAsync(CostEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = (entry.TenantId, entry.At.Year, entry.At.Month);
        _ledger.AddOrUpdate(key, entry.UsdCost, (_, existing) => existing + entry.UsdCost);
        return Task.CompletedTask;
    }

    public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct)
    {
        var key = (tenantId, month.Year, month.Month);
        var mtd = _ledger.TryGetValue(key, out var v) ? v : 0m;
        var percent = DefaultCapUsd > 0 ? (float)(mtd / DefaultCapUsd) : 0f;
        return Task.FromResult(new CostSummary(tenantId, mtd, DefaultCapUsd, percent));
    }
}
