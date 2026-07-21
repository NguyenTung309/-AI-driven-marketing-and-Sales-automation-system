using System.Globalization;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed record ReportSnapshotRow(
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int Conversions,
    double AvgResponseTimeSec,
    double AdSpend);

/// <summary>
/// Core report logic shared by <see cref="ReportAgentGrpcService"/> and the orchestration
/// report adapter. Validation throws plain exceptions (ArgumentException) so callers map them
/// to their own transport (gRPC status vs orchestration AgentResult.Error).
/// </summary>
public sealed class ReportAgentRunner(
    AppDbContext db,
    IAnomalyDetector anomalyDetector,
    IForecaster forecaster)
{
    private readonly AppDbContext _db = db;
    private readonly IAnomalyDetector _anomalyDetector = anomalyDetector;
    private readonly IForecaster _forecaster = forecaster;

    public async Task<IReadOnlyList<ReportSnapshotRow>> DailySnapshotAsync(
        Guid tenantId, string dateRaw, CancellationToken ct)
    {
        var metricDate = ParseDate(dateRaw);
        var rows = await _db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId && k.Date == metricDate)
            .OrderBy(k => k.Platform)
            .Select(k => new ReportSnapshotRow(
                k.Platform,
                k.Leads,
                k.Dms,
                k.Replies,
                k.Conversions,
                (double)(k.AvgResponseTimeSec ?? 0m),
                (double)(k.AdSpend ?? 0m)))
            .ToListAsync(ct).ConfigureAwait(false);

        return rows;
    }

    public async Task<IReadOnlyList<AnomalyPoint>> DetectAnomalyAsync(
        Guid tenantId, string platform, string metric, double zThreshold, int lookbackDays, CancellationToken ct)
    {
        var z = zThreshold > 0 ? zThreshold : 3d;
        var series = await LoadSeriesAsync(tenantId, platform, metric, lookbackDays > 0 ? lookbackDays : 30, ct)
            .ConfigureAwait(false);
        return await _anomalyDetector.ScoreAsync(series, z, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ForecastPoint>> ForecastAsync(
        Guid tenantId, string platform, string metric, int horizonDays, CancellationToken ct)
    {
        var horizon = horizonDays > 0 ? horizonDays : 7;
        var series = await LoadSeriesAsync(tenantId, platform, metric, lookbackDays: 90, ct).ConfigureAwait(false);
        return await _forecaster.ForecastAsync(series, horizon, ct).ConfigureAwait(false);
    }

    public static string FormatDate(DateTimeOffset at) =>
        at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<(DateTimeOffset At, double Value)>> LoadSeriesAsync(
        Guid tenantId, string platform, string metric, int lookbackDays, CancellationToken ct)
    {
        var normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? "all" : platform.Trim().ToLowerInvariant();
        var normalizedMetric = NormalizeMetric(metric);
        var rows = await _db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId && k.Platform == normalizedPlatform)
            .OrderByDescending(k => k.Date)
            .Take(Math.Max(1, lookbackDays))
            .OrderBy(k => k.Date)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows
            .Select(row => new
            {
                At = new DateTimeOffset(row.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Value = MetricValue(row, normalizedMetric),
            })
            .Where(x => x.Value.HasValue)
            .Select(x => (x.At, x.Value!.Value))
            .ToList();
    }

    private static DateOnly ParseDate(string date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("date must use YYYY-MM-DD.");
        return parsed;
    }

    private static string NormalizeMetric(string metric)
    {
        var normalized = (metric ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "leads" or "dms" or "replies" or "conversions" or "avg_response_time_sec" or "ad_spend" or "cpl" or "revenue" => normalized,
            "response_time" => "avg_response_time_sec",
            _ => throw new ArgumentException("metric is not supported."),
        };
    }

    private static double? MetricValue(KpiDaily row, string metric) =>
        metric switch
        {
            "leads" => row.Leads,
            "dms" => row.Dms,
            "replies" => row.Replies,
            "conversions" => row.Conversions,
            "avg_response_time_sec" => row.AvgResponseTimeSec.HasValue ? (double)row.AvgResponseTimeSec.Value : null,
            "ad_spend" => row.AdSpend.HasValue ? (double)row.AdSpend.Value : null,
            "cpl" => row.AdSpend.HasValue && row.Leads > 0 ? (double)(row.AdSpend.Value / row.Leads) : null,
            "revenue" => row.Revenue.HasValue ? (double)row.Revenue.Value : null,
            _ => null,
        };
}
