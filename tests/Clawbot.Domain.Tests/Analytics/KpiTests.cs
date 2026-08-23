using Clawbot.Domain.Analytics;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Analytics;

public sealed class KpiDailyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsDefaults()
    {
        var date = new DateOnly(2026, 8, 17);
        var kpi = KpiDaily.Create(TenantId, date, "facebook", Now);

        kpi.TenantId.Should().Be(TenantId);
        kpi.Date.Should().Be(date);
        kpi.Platform.Should().Be("facebook");
        kpi.Leads.Should().Be(0);
        kpi.Dms.Should().Be(0);
        kpi.Replies.Should().Be(0);
        kpi.RepliedDms.Should().Be(0);
        kpi.Conversions.Should().Be(0);
        kpi.AvgResponseTimeSec.Should().BeNull();
        kpi.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Record_SetsAllMetrics()
    {
        var kpi = KpiDaily.Create(TenantId, new DateOnly(2026, 8, 17), "zalo", Now);

        kpi.Record(5, 20, 15, 12, 3, 45.5m);

        kpi.Leads.Should().Be(5);
        kpi.Dms.Should().Be(20);
        kpi.Replies.Should().Be(15);
        kpi.RepliedDms.Should().Be(12);
        kpi.Conversions.Should().Be(3);
        kpi.AvgResponseTimeSec.Should().Be(45.5m);
    }
}

public sealed class KpiForecastTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var date = new DateOnly(2026, 8, 18);
        var forecast = KpiForecast.Create(TenantId, "facebook", "leads", date, 10m, 8m, 12m, Now);

        forecast.TenantId.Should().Be(TenantId);
        forecast.Platform.Should().Be("facebook");
        forecast.Metric.Should().Be("leads");
        forecast.ForecastDate.Should().Be(date);
        forecast.Value.Should().Be(10m);
        forecast.LowerBound.Should().Be(8m);
        forecast.UpperBound.Should().Be(12m);
        forecast.GeneratedAt.Should().Be(Now);
    }

    [Fact]
    public void Record_UpdatesValues()
    {
        var forecast = KpiForecast.Create(TenantId, "fb", "dms", new DateOnly(2026, 8, 18), 10m, 8m, 12m, Now);

        forecast.Record(15m, 12m, 18m, Now.AddHours(1));

        forecast.Value.Should().Be(15m);
        forecast.LowerBound.Should().Be(12m);
        forecast.UpperBound.Should().Be(18m);
        forecast.GeneratedAt.Should().Be(Now.AddHours(1));
    }
}
