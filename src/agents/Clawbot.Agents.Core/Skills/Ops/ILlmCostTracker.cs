using System.Collections.Concurrent;

namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record CostEntry(
    Guid TenantId,
    string AgentCode,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    DateTimeOffset At,
    Guid? ReservationId = null,
    Guid? SessionId = null,
    // IsEstimated: provider không trả usage nên token/cost là ước lượng cục bộ (thấp hơn hóa đơn thật).
    bool IsEstimated = false);

public sealed record CostSummary(Guid TenantId, decimal MonthToDateUsd, decimal CapUsd, float PercentUsed);

public sealed record CostReservationResult(bool Allowed, string? Reason, Guid? ReservationId = null)
{
    public static CostReservationResult Allow(Guid reservationId) => new(true, null, reservationId);
    public static CostReservationResult Deny(string reason) => new(false, reason);
}

public interface ILlmCostTracker : ISkill
{
    Task RecordAsync(CostEntry entry, CancellationToken ct);
    Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct);
}

public interface ILlmCostReservationStore
{
    Task<CostReservationResult> TryReserveAsync(Guid tenantId, decimal estimatedUsd, DateTimeOffset at, CancellationToken ct);
    Task ReleaseReservationAsync(Guid tenantId, Guid reservationId, CancellationToken ct);
}

// Baseline in-memory tracker keyed by (tenant, year-month).
// Records observed spend; cap enforcement lives in OrchestratorCostGuard.
// Vendor swap target: SQLite ledger + OTel gen_ai.cost metric emission.
internal sealed class InMemoryLlmCostTracker : ILlmCostTracker, ILlmCostReservationStore
{
    private const decimal DefaultCapUsd = 200m;

    private readonly ConcurrentDictionary<(Guid Tenant, int Year, int Month), decimal> _ledger = new();
    private readonly ConcurrentDictionary<(Guid Tenant, int Year, int Month), object> _locks = new();
    private readonly ConcurrentDictionary<Guid, (Guid Tenant, int Year, int Month, decimal Usd)> _reservations = new();

    public string Name => "claude-cost-tracker";

    public Task RecordAsync(CostEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Chỉ bỏ qua khi KHÔNG có gì để ghi. Trước đây guard là `UsdCost <= 0` nên lượt có token
        // nhưng rate = 0 (hoặc provider không trả usage) bị nuốt sạch -> ledger rỗng, cap vô hiệu.
        if (entry.UsdCost <= 0m && entry.InputTokens <= 0 && entry.OutputTokens <= 0)
            return Task.CompletedTask;

        var key = (entry.TenantId, entry.At.Year, entry.At.Month);
        if (entry.ReservationId is { } reservationId && _reservations.TryRemove(reservationId, out var reservation) && reservation.Tenant == entry.TenantId)
        {
            var gate = _locks.GetOrAdd(key, _ => new object());
            lock (gate)
            {
                _ledger.AddOrUpdate(key, entry.UsdCost, (_, existing) => Math.Max(0m, existing - reservation.Usd) + entry.UsdCost);
                return Task.CompletedTask;
            }
        }

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

    public Task<CostReservationResult> TryReserveAsync(Guid tenantId, decimal estimatedUsd, DateTimeOffset at, CancellationToken ct)
    {
        var key = (tenantId, at.Year, at.Month);
        var gate = _locks.GetOrAdd(key, _ => new object());
        var cost = Math.Max(0m, estimatedUsd);
        lock (gate)
        {
            var mtd = _ledger.TryGetValue(key, out var current) ? current : 0m;
            if (mtd + cost > DefaultCapUsd)
                return Task.FromResult(CostReservationResult.Deny("cost_cap_midrun"));

            var reservationId = Guid.NewGuid();
            _ledger.AddOrUpdate(key, cost, (_, existing) => existing + cost);
            _reservations[reservationId] = (tenantId, at.Year, at.Month, cost);
            return Task.FromResult(CostReservationResult.Allow(reservationId));
        }
    }

    public Task ReleaseReservationAsync(Guid tenantId, Guid reservationId, CancellationToken ct)
    {
        if (!_reservations.TryRemove(reservationId, out var reservation) || reservation.Tenant != tenantId)
            return Task.CompletedTask;

        var key = (reservation.Tenant, reservation.Year, reservation.Month);
        var gate = _locks.GetOrAdd(key, _ => new object());
        lock (gate)
        {
            _ledger.AddOrUpdate(key, 0m, (_, existing) => Math.Max(0m, existing - reservation.Usd));
            return Task.CompletedTask;
        }
    }
}
