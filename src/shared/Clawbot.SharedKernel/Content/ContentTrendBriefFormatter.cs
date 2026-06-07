using System.Globalization;
using System.Text;

namespace Clawbot.SharedKernel.Content;

public sealed record ContentTrendBrief(
    string WeekOf,
    string Topic,
    string Source,
    string Metric,
    double RelevanceScore,
    IReadOnlyList<string> ContentIdeas);

public static class ContentTrendBriefFormatter
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static string CurrentWeekOf(DateTimeOffset utcNow)
    {
        var local = utcNow.ToOffset(VietnamOffset).DateTime;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ISOWeek.GetYear(local):0000}-W{ISOWeek.GetWeekOfYear(local):00}");
    }

    public static string NormalizeWeekOfOrCurrent(string? weekOf, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(weekOf))
            return CurrentWeekOf(utcNow);

        if (TryNormalizeWeekOf(weekOf, out var normalized))
            return normalized;

        throw new ArgumentException("week_of must use ISO week format yyyy-Www.", nameof(weekOf));
    }

    public static bool TryNormalizeWeekOf(string? weekOf, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(weekOf))
            return false;

        var candidate = weekOf.Trim().ToUpperInvariant();
        if (candidate.Length != 8
            || candidate[4] != '-'
            || candidate[5] != 'W'
            || !int.TryParse(candidate[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(candidate[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var week)
            || year is < 1 or > 9999
            || week is < 1 or > 53)
        {
            return false;
        }

        normalized = string.Create(CultureInfo.InvariantCulture, $"{year:0000}-W{week:00}");
        return true;
    }

    public static string Marker(string weekOf, string topic)
    {
        if (!TryNormalizeWeekOf(weekOf, out var normalizedWeek))
            throw new ArgumentException("week_of must use ISO week format yyyy-Www.", nameof(weekOf));
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("topic required.", nameof(topic));

        return $"[trend:{normalizedWeek}] {CleanLine(topic)}";
    }

    public static string Format(ContentTrendBrief trend)
    {
        ArgumentNullException.ThrowIfNull(trend);
        var marker = Marker(trend.WeekOf, trend.Topic);
        if (string.IsNullOrWhiteSpace(trend.Source))
            throw new ArgumentException("source required.", nameof(trend));

        var sb = new StringBuilder();
        sb.AppendLine(marker);
        sb.Append("Source: ").AppendLine(CleanLine(trend.Source));
        sb.Append("Metric: ").AppendLine(CleanLine(trend.Metric));
        sb.AppendLine(
            string.Create(CultureInfo.InvariantCulture, $"Score: {trend.RelevanceScore:0.####}"));
        sb.AppendLine("Ideas:");
        foreach (var idea in trend.ContentIdeas.Where(i => !string.IsNullOrWhiteSpace(i)))
        {
            sb.Append("- ").AppendLine(CleanLine(idea));
        }

        return sb.ToString().TrimEnd();
    }

    public static bool IsTrendBrief(string? brief, string? weekOf = null)
    {
        if (!TryParseMarker(FirstLine(brief), out var parsedWeek, out _))
            return false;

        return string.IsNullOrWhiteSpace(weekOf)
            || (TryNormalizeWeekOf(weekOf, out var normalized) && parsedWeek == normalized);
    }

    public static bool TryParse(string? brief, out ContentTrendBrief? trend)
    {
        trend = null;
        if (string.IsNullOrWhiteSpace(brief))
            return false;

        var lines = brief.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        if (!TryParseMarker(lines.FirstOrDefault(), out var weekOf, out var topic))
            return false;

        var source = ReadValue(lines, "Source:");
        var metric = ReadValue(lines, "Metric:");
        var scoreRaw = ReadValue(lines, "Score:");
        if (string.IsNullOrWhiteSpace(source)
            || !double.TryParse(scoreRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
        {
            return false;
        }

        var ideas = new List<string>();
        var ideasIndex = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), "Ideas:", StringComparison.OrdinalIgnoreCase));
        if (ideasIndex >= 0)
        {
            foreach (var line in lines.Skip(ideasIndex + 1))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                    ideas.Add(trimmed[2..].Trim());
            }
        }

        trend = new ContentTrendBrief(weekOf, topic, source, metric, score, ideas);
        return true;
    }

    private static string? FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var index = normalized.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? normalized : normalized[..index];
    }

    private static bool TryParseMarker(string? line, out string weekOf, out string topic)
    {
        weekOf = string.Empty;
        topic = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("[trend:", StringComparison.Ordinal))
            return false;

        var close = line.IndexOf(']', StringComparison.Ordinal);
        if (close <= "[trend:".Length)
            return false;

        var rawWeek = line["[trend:".Length..close];
        if (!TryNormalizeWeekOf(rawWeek, out weekOf))
            return false;

        topic = line[(close + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(topic);
    }

    private static string ReadValue(IEnumerable<string> lines, string prefix)
    {
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line.TrimStart()[prefix.Length..].Trim();
        }

        return string.Empty;
    }

    private static string CleanLine(string value) =>
        value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}
