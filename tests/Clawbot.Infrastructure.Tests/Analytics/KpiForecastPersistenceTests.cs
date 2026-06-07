using Clawbot.Domain.Analytics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Tests.Analytics;

public sealed class KpiForecastPersistenceTests
{
    [Fact]
    public async Task Kpi_forecast_persists_metric_bounds_and_unique_key()
    {
        using var fx = new TestAppDb();
        var forecastDate = new DateOnly(2026, 6, 8);
        var generatedAt = new DateTimeOffset(2026, 6, 7, 18, 0, 0, TimeSpan.Zero);

        var forecast = KpiForecast.Create(
            fx.TenantId,
            "facebook",
            "leads",
            forecastDate,
            value: 42.5m,
            lowerBound: 35m,
            upperBound: 50m,
            generatedAt);

        fx.Db.KpiForecasts.Add(forecast);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var saved = await fx.Db.KpiForecasts.IgnoreQueryFilters().SingleAsync();
        saved.TenantId.Should().Be(fx.TenantId);
        saved.Platform.Should().Be("facebook");
        saved.Metric.Should().Be("leads");
        saved.ForecastDate.Should().Be(forecastDate);
        saved.Value.Should().Be(42.5m);
        saved.LowerBound.Should().Be(35m);
        saved.UpperBound.Should().Be(50m);
        saved.GeneratedAt.Should().Be(generatedAt);
    }
}

