namespace Clawbot.Api.Contracts.Analytics;

public sealed record KpiDailyDto(
    DateOnly Date,
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int RepliedDms,
    int Conversions,
    decimal? AvgResponseTimeSec);

public sealed record OmniChannelRowDto(
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int RepliedDms,
    int Conversions,
    decimal? AvgResponseTimeSec);

public sealed record OmniChannelResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<OmniChannelRowDto> Rows,
    bool Stale);

// Report-1: per-metric comparison vs the prior period (dod = day-over-day, wow = week-over-week).
public sealed record MetricDeltaDto(
    string Metric,
    decimal Current,
    decimal Previous,
    decimal? DeltaPct);

public sealed record OmniChannelDeltaResponse(
    DateOnly From,
    DateOnly To,
    string Compare,
    DateOnly PrevFrom,
    DateOnly PrevTo,
    IReadOnlyList<MetricDeltaDto> Metrics);

public sealed record FunnelDto(
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int Conversions,
    decimal DmRate,
    decimal ReplyRate,
    decimal ConversionRate);

public sealed record AgentPerformanceDto(
    Guid? AgentId,
    string AgentName,
    int Sessions,
    int CompletedSessions,
    int TraceCount,
    decimal CompletionRate,
    int QualitySamples,
    int PassedQualitySamples,
    decimal QualityPassRate,
    decimal? AverageQualityScore);

public sealed record AnomalyDto(
    string Date,
    string Platform,
    string Metric,
    double Value,
    double ZScore,
    bool IsAnomaly);

public sealed record ForecastDto(
    string Date,
    string Platform,
    string Metric,
    double Value,
    double LowerBound,
    double UpperBound);

