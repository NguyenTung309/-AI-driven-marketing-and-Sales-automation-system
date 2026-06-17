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
public sealed class DbClaudeCostTracker(IServiceScopeFactory scopeFactory) : IClaudeCostTracker
{
    private const decimal DefaultCapUsd = 200m;

    public string Name => "claude-cost-tracker";

    public async Task RecordAsync(CostEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var monthToDate = await MonthToDateAsync(db, entry.TenantId, entry.At, ct).ConfigureAwait(false);
        if (monthToDate + entry.UsdCost > DefaultCapUsd)
        {
            return;
        }

        db.ClaudeCostLedger.Add(ClaudeCostEntry.Create(
            entry.TenantId, entry.AgentCode, entry.Model,
            entry.InputTokens, entry.OutputTokens, entry.UsdCost, entry.At));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mtd = await MonthToDateAsync(db, tenantId, month, ct).ConfigureAwait(false);
        var percent = DefaultCapUsd > 0 ? (float)(mtd / DefaultCapUsd) : 0f;
        return new CostSummary(tenantId, mtd, DefaultCapUsd, percent);
    }

    private static async Task<decimal> MonthToDateAsync(
        AppDbContext db,
        Guid tenantId,
        DateTimeOffset month,
        CancellationToken ct)
    {
        var start = new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);
        var costs = await db.ClaudeCostLedger
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.CreatedAt >= start && c.CreatedAt < end)
            .Select(c => c.Usd)
            .ToListAsync(ct).ConfigureAwait(false);

        return costs.Sum();
    }
}
