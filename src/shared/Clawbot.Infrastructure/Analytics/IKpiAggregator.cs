namespace Clawbot.Infrastructure.Analytics;

public sealed record KpiAggregateRow(
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int Conversions,
    decimal? AvgResponseTimeSec);

public interface IKpiAggregator
{
    Task<IReadOnlyList<KpiAggregateRow>> AggregateDailyAsync(
        Guid tenantId,
        DateOnly metricDate,
        CancellationToken ct = default);
}
