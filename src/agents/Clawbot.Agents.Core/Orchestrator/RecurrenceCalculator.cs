using System.Globalization;

namespace Clawbot.Agents.Core.Orchestrator;

public static class RecurrenceCalculator
{
    public static DateTimeOffset NextRunUtc(string cadence, DateTimeOffset fromUtc, string timezoneId)
    {
        var zone = FindZone(timezoneId);
        var local = TimeZoneInfo.ConvertTime(fromUtc, zone);
        var nextLocal = Normalize(cadence) switch
        {
            "daily" => local.DateTime.AddDays(1),
            "weekly" => local.DateTime.AddDays(7),
            "monthly" => AddMonthsClamped(local.DateTime, 1),
            "quarterly" => AddMonthsClamped(local.DateTime, 3),
            var value => throw new ArgumentOutOfRangeException(nameof(cadence), value, "Unsupported cadence."),
        };
        return TimeZoneInfo.ConvertTimeToUtc(nextLocal, zone);
    }

    public static string WindowKey(string cadence, DateTimeOffset dueAtUtc, string timezoneId)
    {
        var local = TimeZoneInfo.ConvertTime(dueAtUtc, FindZone(timezoneId)).DateTime;
        return Normalize(cadence) switch
        {
            "daily" => $"daily:{local:yyyy-MM-dd}",
            "weekly" => $"weekly:{ISOWeek.GetYear(local)}-W{ISOWeek.GetWeekOfYear(local):00}",
            "monthly" => $"monthly:{local:yyyy-MM}",
            "quarterly" => $"quarterly:{local:yyyy}-Q{((local.Month - 1) / 3) + 1}",
            var value => throw new ArgumentOutOfRangeException(nameof(cadence), value, "Unsupported cadence."),
        };
    }

    private static DateTime AddMonthsClamped(DateTime local, int months)
    {
        var target = new DateTime(local.Year, local.Month, 1, local.Hour, local.Minute, local.Second, local.Kind).AddMonths(months);
        var day = Math.Min(local.Day, DateTime.DaysInMonth(target.Year, target.Month));
        return new DateTime(target.Year, target.Month, day, local.Hour, local.Minute, local.Second, local.Kind);
    }

    private static string Normalize(string cadence) => cadence.Trim().ToLowerInvariant();

    private static TimeZoneInfo FindZone(string timezoneId) => TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
}
