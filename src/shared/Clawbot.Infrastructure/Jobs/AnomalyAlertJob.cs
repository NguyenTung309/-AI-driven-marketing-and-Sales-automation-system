using Clawbot.Agents.Contracts.Report;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AnomalyAlertJob(
    AppDbContext db,
    ReportAgent.ReportAgentClient reportAgent,
    IContentNotifier notifier,
    IClock clock,
    ILogger<AnomalyAlertJob> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ReportAgent.ReportAgentClient _reportAgent = reportAgent;
    private readonly IContentNotifier _notifier = notifier;
    private readonly IClock _clock = clock;
    private readonly ILogger<AnomalyAlertJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var tenantId in tenants)
        {
            var rows = await _db.KpiDailies.IgnoreQueryFilters()
                .Where(k => k.TenantId == tenantId)
                .OrderBy(k => k.Date)
                .ToListAsync(ct).ConfigureAwait(false);

            await NotifyIfStaleAsync(tenantId, rows, ct).ConfigureAwait(false);

            foreach (var platformRows in rows.GroupBy(k => k.Platform))
            {
                await NotifyCplSpikeAsync(tenantId, platformRows.Key, platformRows.ToList(), ct).ConfigureAwait(false);
                await NotifyVolumeDropAsync(tenantId, platformRows.Key, "leads", platformRows.ToList(), r => r.Leads, ct).ConfigureAwait(false);
                await NotifyVolumeDropAsync(tenantId, platformRows.Key, "conversions", platformRows.ToList(), r => r.Conversions, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task NotifyIfStaleAsync(Guid tenantId, IReadOnlyList<KpiDaily> rows, CancellationToken ct)
    {
        var latest = rows.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        if (latest is not null && latest.CreatedAt >= _clock.UtcNow.AddHours(-36))
            return;

        await NotifyAsync(
            tenantId,
            "stale",
            "all",
            "kpi_daily",
            "warning",
            "KPI data is stale or missing.",
            ct).ConfigureAwait(false);
    }

    private async Task NotifyCplSpikeAsync(Guid tenantId, string platform, IReadOnlyList<KpiDaily> rows, CancellationToken ct)
    {
        var series = rows
            .Where(r => r.AdSpend.HasValue && r.Leads > 0)
            .Select(r => (At: new DateTimeOffset(r.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), Value: (double)(r.AdSpend!.Value / r.Leads)))
            .ToList();
        if (series.Count < 4)
            return;

        var scored = await _reportAgent.DetectAnomalyAsync(new DetectAnomalyRequest
        {
            TenantId = tenantId.ToString(),
            Platform = platform,
            Metric = "cpl",
            ZThreshold = 3d,
            LookbackDays = 14,
        }, cancellationToken: ct);
        var latest = series[^1].Value;
        var baseline = series.Take(series.Count - 1).TakeLast(7).Average(p => p.Value);
        if (scored.Points.LastOrDefault()?.IsAnomaly == true || latest > baseline * 1.5d)
        {
            await NotifyAsync(
                tenantId,
                "cpl_spike",
                platform,
                "cpl",
                "critical",
                "CPL spike detected.",
                ct).ConfigureAwait(false);
        }
    }

    private async Task NotifyVolumeDropAsync(
        Guid tenantId,
        string platform,
        string metric,
        IReadOnlyList<KpiDaily> rows,
        Func<KpiDaily, int> selector,
        CancellationToken ct)
    {
        var series = rows.Select(r => selector(r)).ToList();
        if (series.Count < 4)
            return;

        var latest = series[^1];
        var baseline = series.Take(series.Count - 1).TakeLast(7).Average();
        if (baseline > 0 && latest < baseline * 0.5d)
        {
            await NotifyAsync(
                tenantId,
                "volume_drop",
                platform,
                metric,
                "warning",
                $"{metric} volume dropped below 50% of the recent average.",
                ct).ConfigureAwait(false);
        }
    }

    private async Task NotifyAsync(
        Guid tenantId,
        string alertType,
        string platform,
        string metric,
        string severity,
        string message,
        CancellationToken ct)
    {
        LogAnalyticsAlert(_logger, tenantId, alertType, platform, metric);
        await _notifier.NotifyAnalyticsAlertAsync(
            tenantId,
            new AnalyticsAlertEvent(tenantId, alertType, platform, metric, severity, message, _clock.UtcNow),
            ct).ConfigureAwait(false);

    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "Analytics alert {AlertType} for tenant {TenantId}, platform {Platform}, metric {Metric}")]
    private static partial void LogAnalyticsAlert(ILogger logger, Guid tenantId, string alertType, string platform, string metric);
}
