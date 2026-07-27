using System.Collections.Concurrent;

namespace Clawbot.Infrastructure.Observability;

/// <summary>
/// In-memory request counters flushed periodically into dbo.request_stats_hourly.
/// Thread-safe; never throws into the request path.
/// </summary>
public sealed class RequestStatsCounter
{
    private readonly ConcurrentDictionary<RequestStatsKey, long> _counts = new();

    public void Increment(Guid? tenantId, int statusCode, DateTimeOffset utcNow)
    {
        try
        {
            var key = new RequestStatsKey(
                RequestStatsHourlyBucket.Truncate(utcNow),
                tenantId ?? Guid.Empty,
                StatusClassOf(statusCode));
            _counts.AddOrUpdate(key, 1, static (_, current) => current + 1);
        }
        catch
        {
            // never affect HTTP
        }
    }

    public IReadOnlyList<RequestStatsSnapshot> SnapshotAndReset()
    {
        if (_counts.IsEmpty) return Array.Empty<RequestStatsSnapshot>();

        var snapshot = new List<RequestStatsSnapshot>(_counts.Count);
        foreach (var key in _counts.Keys)
        {
            if (_counts.TryRemove(key, out var count) && count > 0)
                snapshot.Add(new RequestStatsSnapshot(key.BucketHour, key.TenantId, key.StatusClass, count));
        }

        return snapshot;
    }

    public static string StatusClassOf(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "2xx",
        >= 400 and < 500 => "4xx",
        >= 500 and < 600 => "5xx",
        _ => "other",
    };
}

public readonly record struct RequestStatsKey(DateTimeOffset BucketHour, Guid TenantId, string StatusClass);

public readonly record struct RequestStatsSnapshot(
    DateTimeOffset BucketHour,
    Guid TenantId,
    string StatusClass,
    long Count);

internal static class RequestStatsHourlyBucket
{
    public static DateTimeOffset Truncate(DateTimeOffset at) =>
        new(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero);
}
