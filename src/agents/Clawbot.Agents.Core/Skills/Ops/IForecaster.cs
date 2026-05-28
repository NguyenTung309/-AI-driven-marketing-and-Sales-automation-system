namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record ForecastPoint(DateTimeOffset At, double Forecast, double LowerBound, double UpperBound);

public interface IForecaster : ISkill
{
    Task<IReadOnlyList<ForecastPoint>> ForecastAsync(IReadOnlyList<(DateTimeOffset At, double Value)> history, int horizonDays, CancellationToken ct);
}

// Source: ML.NET TimeSeries SSA (Singular Spectrum Analysis).
// Doc: https://learn.microsoft.com/en-us/dotnet/machine-learning/tutorials/time-series-demand-forecasting
internal sealed class MlNetForecaster : IForecaster
{
    public string Name => "forecast-7day";

    public Task<IReadOnlyList<ForecastPoint>> ForecastAsync(IReadOnlyList<(DateTimeOffset At, double Value)> history, int horizonDays, CancellationToken ct) =>
        throw new NotImplementedException("TODO: MLContext.Forecasting.ForecastBySsa with windowSize tuned to data cadence.");
}
