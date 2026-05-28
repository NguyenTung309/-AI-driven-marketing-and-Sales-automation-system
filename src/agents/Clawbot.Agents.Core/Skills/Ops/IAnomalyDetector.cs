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

    public Task<IReadOnlyList<AnomalyPoint>> ScoreAsync(IReadOnlyList<(DateTimeOffset At, double Value)> series, double zThreshold, CancellationToken ct) =>
        throw new NotImplementedException("TODO: MathNet Statistics.Mean/StandardDeviation over rolling window.");
}
