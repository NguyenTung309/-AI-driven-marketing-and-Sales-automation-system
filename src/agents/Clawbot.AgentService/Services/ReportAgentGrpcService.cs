using Clawbot.Agents.Contracts.Report;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Infrastructure.Persistence;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class ReportAgentGrpcService(
    AppDbContext db,
    IAnomalyDetector anomalyDetector,
    IForecaster forecaster) : ReportAgent.ReportAgentBase
{
    private readonly ReportAgentRunner _runner = new(db, anomalyDetector, forecaster);

    public override async Task<DailySnapshotResponse> DailySnapshot(DailySnapshotRequest request, ServerCallContext context)
    {
        var tenantId = ParseTenantId(request.TenantId);
        IReadOnlyList<ReportSnapshotRow> rows;
        try
        {
            rows = await _runner.DailySnapshotAsync(tenantId, request.Date, context.CancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var response = new DailySnapshotResponse();
        response.Rows.AddRange(rows.Select(r => new PlatformKpi
        {
            Platform = r.Platform,
            Leads = r.Leads,
            Dms = r.Dms,
            Replies = r.Replies,
            Conversions = r.Conversions,
            AvgResponseTimeSec = r.AvgResponseTimeSec,
            AdSpend = r.AdSpend,
        }));
        return response;
    }

    public override async Task<DetectAnomalyResponse> DetectAnomaly(DetectAnomalyRequest request, ServerCallContext context)
    {
        var tenantId = ParseTenantId(request.TenantId);
        try
        {
            var points = await _runner.DetectAnomalyAsync(
                tenantId, request.Platform, request.Metric, request.ZThreshold, request.LookbackDays, context.CancellationToken)
                .ConfigureAwait(false);

            var response = new DetectAnomalyResponse();
            response.Points.AddRange(points.Select(p => new AnomalyPointDto
            {
                Date = ReportAgentRunner.FormatDate(p.At),
                Value = p.Value,
                ZScore = p.ZScore,
                IsAnomaly = p.IsAnomaly,
            }));
            return response;
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    public override async Task<ForecastResponse> Forecast(ForecastRequest request, ServerCallContext context)
    {
        var tenantId = ParseTenantId(request.TenantId);
        try
        {
            var points = await _runner.ForecastAsync(
                tenantId, request.Platform, request.Metric, request.HorizonDays, context.CancellationToken)
                .ConfigureAwait(false);

            var response = new ForecastResponse();
            response.Points.AddRange(points.Select(p => new ForecastPointDto
            {
                Date = ReportAgentRunner.FormatDate(p.At),
                Value = p.Forecast,
                LowerBound = p.LowerBound,
                UpperBound = p.UpperBound,
            }));
            return response;
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    private static Guid ParseTenantId(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var parsed) || parsed == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id must be a valid GUID."));
        return parsed;
    }
}
