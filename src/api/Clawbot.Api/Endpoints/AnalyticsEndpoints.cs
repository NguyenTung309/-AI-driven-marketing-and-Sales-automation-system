using System.Globalization;
using System.Text;
using Clawbot.Agents.Contracts.Report;
using Clawbot.Api.Contracts.Analytics;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalytics(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/analytics").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/omnichannel", GetOmnichannelAsync);
        grp.MapGet("/omnichannel-delta", GetOmnichannelDeltaAsync);
        grp.MapGet("/funnel", GetFunnelAsync);
        grp.MapGet("/agent-performance", GetAgentPerformanceAsync);
        grp.MapGet("/anomalies", GetAnomaliesAsync);
        grp.MapGet("/forecast", GetForecastAsync);
        grp.MapGet("/export", ExportAsync);
        grp.MapGet("/agent-cost", AgentCostAsync);

        return app;
    }

    // M25 — per-agent Claude cost from the ledger (agent nào tốn nhất, trung bình/cuộc).
    private static async Task<IResult> AgentCostAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var toDate = DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedTo)
            ? parsedTo : DateTimeOffset.UtcNow;
        var fromDate = DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedFrom)
            ? parsedFrom : toDate.AddDays(-30);

        var items = await db.ClaudeCostLedger
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.CreatedAt >= fromDate && c.CreatedAt <= toDate)
            .GroupBy(c => c.AgentCode)
            .Select(g => new
            {
                AgentCode = g.Key,
                Calls = g.Count(),
                InputTokens = g.Sum(x => x.InputTokens),
                OutputTokens = g.Sum(x => x.OutputTokens),
                Usd = g.Sum(x => x.Usd),
                AvgUsdPerCall = g.Average(x => x.Usd),
            })
            .OrderByDescending(x => x.Usd)
            .ToListAsync(ct);

        return Results.Ok(new { from = fromDate, to = toDate, items });
    }

    private static async Task<IResult> GetOmnichannelAsync(
        AnalyticsAggregationService analytics,
        ITenantAccessor tenants,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var range = ParseRange(from, to);
        if (range.Error is not null) return range.Error;

        var response = await analytics.GetOmnichannelAsync(tenant.TenantId, range.From, range.To, ct)
            .ConfigureAwait(false);
        return Results.Ok(response);
    }

    // Report-1: per-metric delta vs prior period (compare=dod|wow).
    private static async Task<IResult> GetOmnichannelDeltaAsync(
        AnalyticsAggregationService analytics,
        ITenantAccessor tenants,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? compare,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var range = ParseRange(from, to);
        if (range.Error is not null) return range.Error;

        var response = await analytics.GetOmnichannelDeltaAsync(
            tenant.TenantId, range.From, range.To, compare ?? "dod", ct).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetFunnelAsync(
        AnalyticsAggregationService analytics,
        ITenantAccessor tenants,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? platform,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var range = ParseRange(from, to);
        if (range.Error is not null) return range.Error;

        var response = await analytics.GetFunnelAsync(tenant.TenantId, range.From, range.To, platform, ct)
            .ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetAgentPerformanceAsync(
        AnalyticsAggregationService analytics,
        ITenantAccessor tenants,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var range = ParseRange(from, to);
        if (range.Error is not null) return range.Error;

        var response = await analytics.GetAgentPerformanceAsync(tenant.TenantId, range.From, range.To, ct)
            .ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetAnomaliesAsync(
        ITenantAccessor tenants,
        ReportAgent.ReportAgentClient reportAgent,
        [FromQuery] string metric,
        [FromQuery] string? platform,
        [FromQuery] double zThreshold = 3d,
        [FromQuery] int lookbackDays = 14,
        CancellationToken ct = default)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(metric))
            return Results.BadRequest(new { error = "metric required" });

        try
        {
            var response = await reportAgent.DetectAnomalyAsync(new DetectAnomalyRequest
            {
                TenantId = tenant.TenantId.ToString(),
                Platform = string.IsNullOrWhiteSpace(platform) ? "all" : platform,
                Metric = metric,
                ZThreshold = zThreshold,
                LookbackDays = lookbackDays,
            }, cancellationToken: ct);

            return Results.Ok(response.Points.Select(p => new AnomalyDto(
                p.Date,
                string.IsNullOrWhiteSpace(platform) ? "all" : platform!,
                metric,
                p.Value,
                p.ZScore,
                p.IsAnomaly)).ToList());
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return Results.BadRequest(new { error = ex.Status.Detail });
        }
    }

    private static async Task<IResult> GetForecastAsync(
        AnalyticsAggregationService analytics,
        ITenantAccessor tenants,
        [FromQuery] string metric,
        [FromQuery] string? platform,
        [FromQuery] int horizon = 7,
        CancellationToken ct = default)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(metric))
            return Results.BadRequest(new { error = "metric required" });

        var normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? "all" : platform;
        var response = await analytics.GetForecastAsync(tenant.TenantId, normalizedPlatform, metric, horizon, ct)
            .ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> ExportAsync(
        AnalyticsAggregationService analytics,
        ITenantAccessor tenants,
        [FromQuery] string? format,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var range = ParseRange(from, to);
        if (range.Error is not null) return range.Error;

        string normalizedFormat;
        try
        {
            normalizedFormat = AnalyticsExportService.NormalizeFormat(format);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var rows = (await analytics.GetOmnichannelAsync(tenant.TenantId, range.From, range.To, ct).ConfigureAwait(false)).Rows;
        var fileName = string.Create(CultureInfo.InvariantCulture, $"analytics-{range.From:yyyyMMdd}-{range.To:yyyyMMdd}.{normalizedFormat}");
        return normalizedFormat == "pdf"
            ? Results.File(AnalyticsExportService.BuildPdf(rows), "application/pdf", fileName)
            : Results.File(Encoding.UTF8.GetBytes(AnalyticsExportService.BuildCsv(rows)), "text/csv", fileName);
    }

    private static (DateOnly From, DateOnly To, IResult? Error) ParseRange(string? from, string? to)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var parsedFrom = string.IsNullOrWhiteSpace(from) ? today.AddDays(-6) : ParseDate(from);
        var parsedTo = string.IsNullOrWhiteSpace(to) ? today : ParseDate(to);

        if (parsedFrom is null || parsedTo is null)
            return (default, default, Results.BadRequest(new { error = "from/to must use YYYY-MM-DD" }));
        if (parsedFrom > parsedTo)
            return (default, default, Results.BadRequest(new { error = "from must be before or equal to to" }));

        return (parsedFrom.Value, parsedTo.Value, null);
    }

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}

