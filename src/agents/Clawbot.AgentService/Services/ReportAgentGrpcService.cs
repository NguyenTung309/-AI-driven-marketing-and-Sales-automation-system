using Clawbot.Agents.Contracts.Report;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Clawbot.AgentService.Services;

public sealed class ReportAgentGrpcService(
    AppDbContext db,
    IAnomalyDetector anomalyDetector,
    IForecaster forecaster) : ReportAgent.ReportAgentBase
{
    private readonly AppDbContext _db = db;
    private readonly IAnomalyDetector _anomalyDetector = anomalyDetector;
    private readonly IForecaster _forecaster = forecaster;

    public override async Task<DailySnapshotResponse> DailySnapshot(DailySnapshotRequest request, ServerCallContext context)
    {
        var tenantId = ParseTenantId(request.TenantId);
        var metricDate = ParseDate(request.Date);

        var rows = await _db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId && k.Date == metricDate)
            .OrderBy(k => k.Platform)
            .Select(k => new PlatformKpi
            {
                Platform = k.Platform,
                Leads = k.Leads,
                Dms = k.Dms,
                Replies = k.Replies,
                Conversions = k.Conversions,
                AvgResponseTimeSec = (double)(k.AvgResponseTimeSec ?? 0m),
                AdSpend = (double)(k.AdSpend ?? 0m),
            })
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);

        var response = new DailySnapshotResponse();
        response.Rows.AddRange(rows);
        return response;
    }

    public override async Task<DetectAnomalyResponse> DetectAnomaly(DetectAnomalyRequest request, ServerCallContext context)
    {
        var tenantId = ParseTenantId(request.TenantId);
        var zThreshold = request.ZThreshold > 0 ? request.ZThreshold : 3d;
        var series = await LoadSeriesAsync(
            tenantId,
            request.Platform,
            request.Metric,
            request.LookbackDays > 0 ? request.LookbackDays : 30,
            context.CancellationToken).ConfigureAwait(false);

        var points = await _anomalyDetector.ScoreAsync(series, zThreshold, context.CancellationToken)
            .ConfigureAwait(false);

        var response = new DetectAnomalyResponse();
        response.Points.AddRange(points.Select(p => new AnomalyPointDto
        {
            Date = FormatDate(p.At),
            Value = p.Value,
            ZScore = p.ZScore,
            IsAnomaly = p.IsAnomaly,
        }));
        return response;
    }

    public override async Task<ForecastResponse> Forecast(ForecastRequest request, ServerCallContext context)
    {
        var tenantId = ParseTenantId(request.TenantId);
        var horizonDays = request.HorizonDays > 0 ? request.HorizonDays : 7;
        var series = await LoadSeriesAsync(
            tenantId,
            request.Platform,
            request.Metric,
            lookbackDays: 90,
            context.CancellationToken).ConfigureAwait(false);

        var points = await _forecaster.ForecastAsync(series, horizonDays, context.CancellationToken)
            .ConfigureAwait(false);

        var response = new ForecastResponse();
        response.Points.AddRange(points.Select(p => new ForecastPointDto
        {
            Date = FormatDate(p.At),
            Value = p.Forecast,
            LowerBound = p.LowerBound,
            UpperBound = p.UpperBound,
        }));
        return response;
    }

    private async Task<IReadOnlyList<(DateTimeOffset At, double Value)>> LoadSeriesAsync(
        Guid tenantId,
        string platform,
        string metric,
        int lookbackDays,
        CancellationToken ct)
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

    private static Guid ParseTenantId(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var parsed) || parsed == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id must be a valid GUID."));

        return parsed;
    }

    private static DateOnly ParseDate(string date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "date must use YYYY-MM-DD."));

        return parsed;
    }

    private static string NormalizeMetric(string metric)
    {
        var normalized = metric.Trim().ToLowerInvariant();
        return normalized switch
        {
            "leads" or "dms" or "replies" or "conversions" or "avg_response_time_sec" or "ad_spend" or "cpl" => normalized,
            "response_time" => "avg_response_time_sec",
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "metric is not supported.")),
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
            _ => null,
        };

    private static string FormatDate(DateTimeOffset at) =>
        at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
