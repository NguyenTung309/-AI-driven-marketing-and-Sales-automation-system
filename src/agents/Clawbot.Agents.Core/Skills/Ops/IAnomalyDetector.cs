using MathNet.Numerics.Statistics;

namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record AnomalyPoint(DateTimeOffset At, double Value, double ZScore, bool IsAnomaly);

public interface IAnomalyDetector : ISkill
{
    Task<IReadOnlyList<AnomalyPoint>> ScoreAsync(IReadOnlyList<(DateTimeOffset At, double Value)> series, double zThreshold, CancellationToken ct);
}

// Source: https://numerics.mathdotnet.com/ (z-score using rolling mean + stddev).
internal sealed class ZScoreAnomalyDetector : IAnomalyDetector
{
    public string Name => "anomaly-detection";

    public Task<IReadOnlyList<AnomalyPoint>> ScoreAsync(IReadOnlyList<(DateTimeOffset At, double Value)> series, double zThreshold, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (zThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(zThreshold), "zThreshold must be positive.");

        var ordered = series.OrderBy(p => p.At).ToList();
        var points = new List<AnomalyPoint>(ordered.Count);
        var windowSize = Math.Min(7, Math.Max(3, ordered.Count / 2));

        for (var i = 0; i < ordered.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var current = ordered[i];
            var windowStart = Math.Max(0, i - windowSize);
            var previous = ordered
                .Skip(windowStart)
                .Take(i - windowStart)
                .Select(p => p.Value)
                .ToArray();

            if (previous.Length < 3)
            {
                points.Add(new AnomalyPoint(current.At, current.Value, ZScore: 0d, IsAnomaly: false));
                continue;
            }

            var mean = previous.Mean();
            var stdDev = previous.StandardDeviation();
            if (double.IsNaN(stdDev) || stdDev <= double.Epsilon)
            {
                points.Add(new AnomalyPoint(current.At, current.Value, ZScore: 0d, IsAnomaly: false));
                continue;
            }

            var zScore = (current.Value - mean) / stdDev;
            points.Add(new AnomalyPoint(current.At, current.Value, zScore, Math.Abs(zScore) >= zThreshold));
        }

        return Task.FromResult<IReadOnlyList<AnomalyPoint>>(points);
    }
}
