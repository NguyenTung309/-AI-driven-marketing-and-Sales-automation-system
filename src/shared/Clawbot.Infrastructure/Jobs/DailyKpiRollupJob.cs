using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class DailyKpiRollupJob(AppDbContext db, ILogger<DailyKpiRollupJob> logger)
{
    private const string AggregatePlatform = "all";

    private readonly AppDbContext _db = db;
    private readonly ILogger<DailyKpiRollupJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayStart = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        var inserted = 0;
        foreach (var tenantId in tenants)
        {
            var leadsCount = await _db.Leads.IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId && l.CreatedAt >= dayStart && l.CreatedAt < dayEnd)
                .CountAsync(ct).ConfigureAwait(false);
            var dmsCount = await _db.Conversations.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.CreatedAt >= dayStart && c.CreatedAt < dayEnd)
                .CountAsync(ct).ConfigureAwait(false);
            var repliesCount = await _db.Messages.IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && m.SentAt >= dayStart && m.SentAt < dayEnd && m.Direction == "out")
                .CountAsync(ct).ConfigureAwait(false);
            var conversions = await _db.Leads.IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId && l.Stage == "customer" && l.CreatedAt < dayEnd)
                .CountAsync(ct).ConfigureAwait(false);

            var existing = await _db.KpiDailies.IgnoreQueryFilters()
                .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Date == today && k.Platform == AggregatePlatform, ct)
                .ConfigureAwait(false);

            if (existing is null)
            {
                existing = KpiDaily.Create(tenantId, today, AggregatePlatform, DateTimeOffset.UtcNow);
                _db.KpiDailies.Add(existing);
                inserted++;
            }

            existing.Record(leadsCount, dmsCount, repliesCount, conversions, avgRespSec: null, adSpend: null);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogRolledUp(_logger, inserted, today);
    }

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Daily KPI rollup processed (new={Count}) for {Day}")]
    private static partial void LogRolledUp(ILogger logger, int count, DateOnly day);
}
