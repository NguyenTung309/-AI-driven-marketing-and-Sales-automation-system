using Clawbot.Domain.Common;

namespace Clawbot.Domain.Analytics;

public sealed class KpiForecast : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Metric { get; private set; } = string.Empty;
    public DateOnly ForecastDate { get; private set; }
    public decimal Value { get; private set; }
    public decimal LowerBound { get; private set; }
    public decimal UpperBound { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }

    private KpiForecast() { }

    public static KpiForecast Create(
        Guid tenantId,
        string platform,
        string metric,
        DateOnly forecastDate,
        decimal value,
        decimal lowerBound,
        decimal upperBound,
        DateTimeOffset generatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Platform = platform,
            Metric = metric,
            ForecastDate = forecastDate,
            Value = value,
            LowerBound = lowerBound,
            UpperBound = upperBound,
            GeneratedAt = generatedAt,
        };

    public void Record(decimal value, decimal lowerBound, decimal upperBound, DateTimeOffset generatedAt)
    {
        Value = value;
        LowerBound = lowerBound;
        UpperBound = upperBound;
        GeneratedAt = generatedAt;
    }
}

