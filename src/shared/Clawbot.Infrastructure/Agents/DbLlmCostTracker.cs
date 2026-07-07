using System.Data;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Agents;

/// <summary>
/// Persists each Claude call into <c>claude_cost_ledger</c> (replaces the in-memory tracker).
/// Resolves a scoped <see cref="AppDbContext"/> per call so it can be registered as a singleton
/// (safe for singleton/transient consumers — no captive dependency).
/// </summary>
public sealed class DbLlmCostTracker(IServiceScopeFactory scopeFactory) : ILlmCostTracker, ILlmCostReservationStore
{
    // Mặc định khi tenant chưa đặt hạn mức riêng (Tenant.MonthlyCostCapUsd = null).
    private const decimal DefaultCapUsd = 200m;

    public string Name => "llm-cost-tracker";

    public async Task RecordAsync(CostEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (entry.UsdCost <= 0m)
            return;

        if (entry.ReservationId is { } reservationId)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
            var reservation = await FindReservationAsync(db, entry.TenantId, reservationId, ct).ConfigureAwait(false);
            if (reservation is not null)
                reservation.ApplyReservation(entry.AgentCode, entry.Model, entry.InputTokens, entry.OutputTokens, entry.UsdCost, entry.SessionId);
            else
                db.LlmCostLedger.Add(LlmCostEntry.Create(
                    entry.TenantId, entry.AgentCode, entry.Model,
                    entry.InputTokens, entry.OutputTokens, entry.UsdCost, entry.At, entry.SessionId));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return;
        }

        db.LlmCostLedger.Add(LlmCostEntry.Create(
            entry.TenantId, entry.AgentCode, entry.Model,
            entry.InputTokens, entry.OutputTokens, entry.UsdCost, entry.At, entry.SessionId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mtd = await MonthToDateAsync(db, tenantId, month, ct).ConfigureAwait(false);
        var cap = await ResolveCapAsync(db, tenantId, ct).ConfigureAwait(false);
        var percent = cap > 0 ? (float)(mtd / cap) : 0f;
        return new CostSummary(tenantId, mtd, cap, percent);
    }

    // Hạn mức hiệu lực = cap riêng của tenant, hoặc mặc định hệ thống khi chưa đặt.
    private static async Task<decimal> ResolveCapAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        var cap = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => t.MonthlyCostCapUsd)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return cap is > 0m ? cap.Value : DefaultCapUsd;
    }

    public async Task<CostReservationResult> TryReserveAsync(
        Guid tenantId,
        decimal estimatedUsd,
        DateTimeOffset at,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        var cost = Math.Max(0m, estimatedUsd);
        var mtd = await MonthToDateAsync(db, tenantId, at, ct).ConfigureAwait(false);
        var cap = await ResolveCapAsync(db, tenantId, ct).ConfigureAwait(false);
        if (mtd + cost > cap)
            return CostReservationResult.Deny("cost_cap_midrun");

        var reservationId = Guid.NewGuid();
        if (cost > 0m)
        {
            db.LlmCostLedger.Add(LlmCostEntry.CreateReservation(tenantId, reservationId, cost, at));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return CostReservationResult.Allow(reservationId);
    }

    public async Task ReleaseReservationAsync(Guid tenantId, Guid reservationId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        var reservation = await FindReservationAsync(db, tenantId, reservationId, ct).ConfigureAwait(false);

        if (reservation is not null)
        {
            reservation.ReleaseReservation();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static Task<LlmCostEntry?> FindReservationAsync(
        AppDbContext db,
        Guid tenantId,
        Guid reservationId,
        CancellationToken ct) =>
        db.LlmCostLedger
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cost =>
                cost.Id == reservationId &&
                cost.TenantId == tenantId &&
                cost.AgentCode == LlmCostEntry.ReservationAgentCode,
                ct);

    private static async Task<decimal> MonthToDateAsync(
        AppDbContext db,
        Guid tenantId,
        DateTimeOffset month,
        CancellationToken ct)
    {
        var start = new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);
        var costs = await db.LlmCostLedger
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.CreatedAt >= start && c.CreatedAt < end)
            .Select(c => c.Usd)
            .ToListAsync(ct).ConfigureAwait(false);

        return costs.Sum();
    }
}
