namespace Clawbot.SharedKernel.Content;

public interface IGoldenHourResolver
{
    DateTimeOffset ResolveNext(string platform, DateTimeOffset utcNow);
}

public sealed class DefaultGoldenHourResolver : IGoldenHourResolver
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly Dictionary<string, TimeOnly> PlatformHours =
        new Dictionary<string, TimeOnly>(StringComparer.OrdinalIgnoreCase)
        {
            ["zalo"] = new(8, 0),
            ["youtube"] = new(18, 0),
            ["instagram"] = new(19, 30),
            ["tiktok"] = new(20, 0),
            ["facebook"] = new(20, 30),
        };

    public DateTimeOffset ResolveNext(string platform, DateTimeOffset utcNow)
    {
        var localNow = utcNow.ToOffset(VietnamOffset);
        var hour = PlatformHours.TryGetValue(platform.Trim(), out var configured)
            ? configured
            : new TimeOnly(19, 0);
        var candidate = new DateTimeOffset(localNow.Date.Add(hour.ToTimeSpan()), VietnamOffset);
        return candidate > localNow ? candidate : candidate.AddDays(1);
    }
}
