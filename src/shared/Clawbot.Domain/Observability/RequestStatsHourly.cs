using Clawbot.Domain.Common;

namespace Clawbot.Domain.Observability;

/// <summary>
/// Hourly request counts by HTTP status class. Written by RequestStatsFlushJob (not audit-intercepted).
/// TenantId = Guid.Empty means "no tenant / system".
/// </summary>
public sealed class RequestStatsHourly : IAuditExempt
{
    public long Id { get; private set; }
    public DateTimeOffset BucketHour { get; private set; }
    public Guid TenantId { get; private set; }
    public string StatusClass { get; private set; } = string.Empty;
    public long Count { get; private set; }

    private RequestStatsHourly() { }

    public static RequestStatsHourly Create(DateTimeOffset bucketHour, Guid tenantId, string statusClass, long count) =>
        new()
        {
            BucketHour = TruncateHour(bucketHour),
            TenantId = tenantId,
            StatusClass = statusClass,
            Count = count,
        };

    public void Add(long delta) => Count += delta;

    public static DateTimeOffset TruncateHour(DateTimeOffset at) =>
        new(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero);
}
