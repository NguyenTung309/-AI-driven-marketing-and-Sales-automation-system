using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.TimeSeries;

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

    public Task<IReadOnlyList<ForecastPoint>> ForecastAsync(IReadOnlyList<(DateTimeOffset At, double Value)> history, int horizonDays, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (horizonDays < 1)
            throw new ArgumentOutOfRangeException(nameof(horizonDays), "horizonDays must be positive.");

        var ordered = history.OrderBy(p => p.At).ToList();
        if (ordered.Count == 0)
            return Task.FromResult<IReadOnlyList<ForecastPoint>>([]);

        ct.ThrowIfCancellationRequested();

        if (ordered.Count < 8)
            return Task.FromResult<IReadOnlyList<ForecastPoint>>(LinearFallback(ordered, horizonDays));

        try
        {
            var ml = new MLContext(seed: 42);
            var data = ml.Data.LoadFromEnumerable(ordered.Select(p => new ForecastInput { Value = (float)p.Value }));
            var trainSize = ordered.Count;
            var windowSize = Math.Min(7, Math.Max(2, trainSize / 3));
            var pipeline = ml.Forecasting.ForecastBySsa(
                outputColumnName: nameof(ForecastOutput.Forecast),
                inputColumnName: nameof(ForecastInput.Value),
                windowSize: windowSize,
                seriesLength: trainSize,
                trainSize: trainSize,
                horizon: horizonDays,
                confidenceLowerBoundColumn: nameof(ForecastOutput.LowerBound),
                confidenceUpperBoundColumn: nameof(ForecastOutput.UpperBound),
                confidenceLevel: 0.95f);

            var model = pipeline.Fit(data);
            var engine = model.CreateTimeSeriesEngine<ForecastInput, ForecastOutput>(ml);
            var output = engine.Predict();
            return Task.FromResult<IReadOnlyList<ForecastPoint>>(MapForecast(ordered[^1].At, output, horizonDays));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult<IReadOnlyList<ForecastPoint>>(LinearFallback(ordered, horizonDays));
        }
    }

    private static List<ForecastPoint> MapForecast(DateTimeOffset lastAt, ForecastOutput output, int horizonDays)
    {
        var points = new List<ForecastPoint>(horizonDays);
        for (var i = 0; i < horizonDays; i++)
        {
            var forecast = SafeValue(output.Forecast, i);
            var lower = SafeValue(output.LowerBound, i, forecast);
            var upper = SafeValue(output.UpperBound, i, forecast);
            if (lower > forecast) lower = forecast;
            if (upper < forecast) upper = forecast;
            points.Add(new ForecastPoint(lastAt.AddDays(i + 1), forecast, lower, upper));
        }

        return points;
    }

    private static List<ForecastPoint> LinearFallback(List<(DateTimeOffset At, double Value)> ordered, int horizonDays)
    {
        var points = new List<ForecastPoint>(horizonDays);
        var last = ordered[^1];
        var previous = ordered.Count > 1 ? ordered[^2].Value : last.Value;
        var step = last.Value - previous;
        var spread = Math.Max(Math.Abs(step), Math.Max(1d, Math.Abs(last.Value) * 0.05d));

        for (var i = 1; i <= horizonDays; i++)
        {
            var forecast = last.Value + (step * i);
            points.Add(new ForecastPoint(last.At.AddDays(i), forecast, forecast - spread, forecast + spread));
        }

        return points;
    }

    private static double SafeValue(float[]? values, int index, double fallback = 0d)
    {
        if (values is null || index >= values.Length || float.IsNaN(values[index]) || float.IsInfinity(values[index]))
            return fallback;

        return values[index];
    }

    private sealed class ForecastInput
    {
        public float Value { get; set; }
    }

    private sealed class ForecastOutput
    {
        [VectorType]
        public float[] Forecast { get; set; } = [];

        [VectorType]
        public float[] LowerBound { get; set; } = [];

        [VectorType]
        public float[] UpperBound { get; set; } = [];
    }
}
