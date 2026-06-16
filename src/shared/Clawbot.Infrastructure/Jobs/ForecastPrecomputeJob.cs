using System.Globalization;
using Clawbot.Agents.Contracts.Report;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class ForecastPrecomputeJob(
    AppDbContext db,
    ReportAgent.ReportAgentClient reportAgent,
    IClock clock,
    ILogger<ForecastPrecomputeJob> logger)
{
    private static readonly string[] Platforms = ["all", "zalo", "facebook", "instagram", "tiktok", "youtube"];
    private static readonly string[] Metrics = ["leads", "dms", "replies", "conversions", "cpl", "ad_spend"];

    private readonly AppDbContext _db = db;
    private readonly ReportAgent.ReportAgentClient _reportAgent = reportAgent;
    private readonly IClock _clock = clock;
    private readonly ILogger<ForecastPrecomputeJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var tenantId in tenants)
        {
            foreach (var platform in Platforms)
            {
                foreach (var metric in Metrics)
                {
                    await PrecomputeAsync(tenantId, platform, metric, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task PrecomputeAsync(Guid tenantId, string platform, string metric, CancellationToken ct)
    {
        try
        {
            var response = await _reportAgent.ForecastAsync(new ForecastRequest
            {
                TenantId = tenantId.ToString(),
                Platform = platform,
                Metric = metric,
                HorizonDays = 7,
            }, cancellationToken: ct);

            foreach (var point in response.Points)
            {
                if (!DateOnly.TryParseExact(point.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var forecastDate))
                    continue;

                var existing = await _db.KpiForecasts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(f =>
                        f.TenantId == tenantId &&
                        f.Platform == platform &&
                        f.Metric == metric &&
                        f.ForecastDate == forecastDate,
                        ct).ConfigureAwait(false);

                if (existing is null)
                {
                    _db.KpiForecasts.Add(KpiForecast.Create(
                        tenantId,
                        platform,
                        metric,
                        forecastDate,
                        (decimal)point.Value,
                        (decimal)point.LowerBound,
                        (decimal)point.UpperBound,
                        _clock.UtcNow));
                }
                else
                {
                    existing.Record((decimal)point.Value, (decimal)point.LowerBound, (decimal)point.UpperBound, _clock.UtcNow);
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            LogForecastSkipped(_logger, tenantId, platform, metric, ex.Status.Detail);
        }
    }

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "Forecast skipped for tenant {TenantId}, platform {Platform}, metric {Metric}: {Reason}")]
    private static partial void LogForecastSkipped(ILogger logger, Guid tenantId, string platform, string metric, string reason);
}

