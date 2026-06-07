namespace Clawbot.Api.Contracts.Analytics;

public sealed record KpiDailyDto(
    DateOnly Date,
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int Conversions,
    decimal? AvgResponseTimeSec,
    decimal? AdSpend,
    decimal? Cpl);

public sealed record OmniChannelRowDto(
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int Conversions,
    decimal? AvgResponseTimeSec,
    decimal? AdSpend,
    decimal? Cpl);

public sealed record OmniChannelResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<OmniChannelRowDto> Rows,
    bool Stale);

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
    decimal CompletionRate);

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

