namespace Clawbot.Infrastructure.Analytics;

public sealed record KpiAggregateRow(
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int RepliedDms,
    int Conversions,
    decimal? AvgResponseTimeSec,
    decimal? AdSpend,
    decimal? Revenue = null);

public interface IKpiAggregator
{
    Task<IReadOnlyList<KpiAggregateRow>> AggregateDailyAsync(
        Guid tenantId,
        DateOnly metricDate,
        CancellationToken ct = default);
}
