using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Report;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ReportAgentGrpcServiceTests
{
    [Fact]
    public async Task DailySnapshot_returns_saved_kpi_rows_for_tenant_and_date()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var metricDate = new DateOnly(2026, 6, 7);
        var facebook = KpiDaily.Create(tenantId, metricDate, "facebook", DateTimeOffset.UtcNow);
        facebook.Record(12, 5, 4, 2, avgRespSec: 123.45m, adSpend: 67.89m);
        var all = KpiDaily.Create(tenantId, metricDate, "all", DateTimeOffset.UtcNow);
        all.Record(12, 5, 4, 2, avgRespSec: 123.45m, adSpend: 67.89m);
        fx.Db.KpiDailies.AddRange(facebook, all);
        await fx.Db.SaveChangesAsync();

        var service = new ReportAgentGrpcService(
            fx.Db,
            Substitute.For<IAnomalyDetector>(),
            Substitute.For<IForecaster>());

        var response = await service.DailySnapshot(
            new DailySnapshotRequest { TenantId = tenantId.ToString(), Date = "2026-06-07" },
            TestServerCallContext.Create());

        response.Rows.Should().HaveCount(2);
        response.Rows.Select(r => r.Platform).Should().Equal("all", "facebook");
        var row = response.Rows.Single(r => r.Platform == "facebook");
        row.Leads.Should().Be(12);
        row.Dms.Should().Be(5);
        row.Replies.Should().Be(4);
        row.Conversions.Should().Be(2);
        row.AvgResponseTimeSec.Should().BeApproximately(123.45d, 0.001d);
        row.AdSpend.Should().BeApproximately(67.89d, 0.001d);
    }

    [Fact]
    public async Task DetectAnomaly_loads_metric_series_and_returns_detector_points()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        AddDaily(fx, tenantId, new DateOnly(2026, 6, 5), "facebook", leads: 10);
        AddDaily(fx, tenantId, new DateOnly(2026, 6, 6), "facebook", leads: 11);
        AddDaily(fx, tenantId, new DateOnly(2026, 6, 7), "facebook", leads: 50);
        await fx.Db.SaveChangesAsync();

        var detector = Substitute.For<IAnomalyDetector>();
        detector.ScoreAsync(Arg.Any<IReadOnlyList<(DateTimeOffset At, double Value)>>(), 2.5d, Arg.Any<CancellationToken>())
            .Returns([
                new AnomalyPoint(new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero), 50d, 4.2d, true),
            ]);
        var service = new ReportAgentGrpcService(fx.Db, detector, Substitute.For<IForecaster>());

        var response = await service.DetectAnomaly(
            new DetectAnomalyRequest
            {
                TenantId = tenantId.ToString(),
                Platform = "facebook",
                Metric = "leads",
                ZThreshold = 2.5d,
                LookbackDays = 14,
            },
            TestServerCallContext.Create());

        response.Points.Should().ContainSingle().Which.Should().BeEquivalentTo(new AnomalyPointDto
        {
            Date = "2026-06-07",
            Value = 50d,
            ZScore = 4.2d,
            IsAnomaly = true,
        });
        await detector.Received(1).ScoreAsync(
            Arg.Is<IReadOnlyList<(DateTimeOffset At, double Value)>>(s => s.Count == 3 && s[0].Value == 10d && s[2].Value == 50d),
            2.5d,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Forecast_loads_metric_series_and_returns_forecast_points()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        AddDaily(fx, tenantId, new DateOnly(2026, 6, 5), "all", leads: 10);
        AddDaily(fx, tenantId, new DateOnly(2026, 6, 6), "all", leads: 12);
        await fx.Db.SaveChangesAsync();

        var forecaster = Substitute.For<IForecaster>();
        forecaster.ForecastAsync(Arg.Any<IReadOnlyList<(DateTimeOffset At, double Value)>>(), 7, Arg.Any<CancellationToken>())
            .Returns([
                new ForecastPoint(new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero), 13d, 11d, 15d),
            ]);
        var service = new ReportAgentGrpcService(fx.Db, Substitute.For<IAnomalyDetector>(), forecaster);

        var response = await service.Forecast(
            new ForecastRequest
            {
                TenantId = tenantId.ToString(),
                Platform = "all",
                Metric = "leads",
                HorizonDays = 7,
            },
            TestServerCallContext.Create());

        response.Points.Should().ContainSingle().Which.Should().BeEquivalentTo(new ForecastPointDto
        {
            Date = "2026-06-07",
            Value = 13d,
            LowerBound = 11d,
            UpperBound = 15d,
        });
        await forecaster.Received(1).ForecastAsync(
            Arg.Is<IReadOnlyList<(DateTimeOffset At, double Value)>>(s => s.Count == 2 && s[0].Value == 10d && s[1].Value == 12d),
            7,
            Arg.Any<CancellationToken>());
    }

    private static void AddDaily(AgentServiceTestAppDb fx, Guid tenantId, DateOnly metricDate, string platform, int leads)
    {
        var row = KpiDaily.Create(tenantId, metricDate, platform, DateTimeOffset.UtcNow);
        row.Record(leads, dms: 0, replies: 0, conversions: 0, avgRespSec: null, adSpend: null);
        fx.Db.KpiDailies.Add(row);
    }
}
