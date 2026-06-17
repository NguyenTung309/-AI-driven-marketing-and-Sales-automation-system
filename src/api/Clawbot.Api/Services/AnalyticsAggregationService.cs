using System.Globalization;
using System.Text.Json;
using Clawbot.Api.Contracts.Analytics;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed class AnalyticsAggregationService(AppDbContext db, IClock clock)
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public async Task<OmniChannelResponse> GetOmnichannelAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var rows = await LoadKpiAsync(tenantId, from, to, platform: null, ct).ConfigureAwait(false);
        var latestCreatedAt = await _db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => (DateTimeOffset?)k.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return new OmniChannelResponse(
            from,
            to,
            BuildOmniRows(rows),
            latestCreatedAt is null || latestCreatedAt < DateTimeOffset.UtcNow.AddHours(-36));
    }

    // Report-1: totals for the period vs the prior period (dod shifts back the range length capped
    // at 1 day, wow shifts back 7 days), with per-metric delta %. Computed on-the-fly from kpi_daily.
    public async Task<OmniChannelDeltaResponse> GetOmnichannelDeltaAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        string compare,
        CancellationToken ct = default)
    {
        var normalized = string.Equals(compare, "wow", StringComparison.OrdinalIgnoreCase) ? "wow" : "dod";
        var shiftDays = normalized == "wow" ? 7 : Math.Max(1, to.DayNumber - from.DayNumber + 1);
        var prevFrom = from.AddDays(-shiftDays);
        var prevTo = to.AddDays(-shiftDays);

        var current = await LoadKpiAsync(tenantId, from, to, platform: null, ct).ConfigureAwait(false);
        var previous = await LoadKpiAsync(tenantId, prevFrom, prevTo, platform: null, ct).ConfigureAwait(false);

        var metrics = new List<MetricDeltaDto>
        {
            BuildDelta("leads", current.Sum(r => r.Leads), previous.Sum(r => r.Leads)),
            BuildDelta("dms", current.Sum(r => r.Dms), previous.Sum(r => r.Dms)),
            BuildDelta("replies", current.Sum(r => r.Replies), previous.Sum(r => r.Replies)),
            BuildDelta("conversions", current.Sum(r => r.Conversions), previous.Sum(r => r.Conversions)),
            BuildDelta("adSpend", SumNullable(current.Select(r => r.AdSpend)) ?? 0m, SumNullable(previous.Select(r => r.AdSpend)) ?? 0m),
            BuildDelta("avgResponseTimeSec", AverageNullable(current.Select(r => r.AvgResponseTimeSec)) ?? 0m, AverageNullable(previous.Select(r => r.AvgResponseTimeSec)) ?? 0m),
        };

        return new OmniChannelDeltaResponse(from, to, normalized, prevFrom, prevTo, metrics);
    }

    private static MetricDeltaDto BuildDelta(string metric, decimal current, decimal previous) =>
        new(metric, current, previous,
            previous == 0m ? null : Math.Round((current - previous) / previous * 100m, 1));

    public async Task<FunnelDto> GetFunnelAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        string? platform,
        CancellationToken ct = default)
    {
        var rows = await LoadKpiAsync(tenantId, from, to, platform, ct).ConfigureAwait(false);
        var platformLabel = string.IsNullOrWhiteSpace(platform) ? "all" : platform.Trim().ToLowerInvariant();
        var leads = rows.Sum(r => r.Leads);
        var dms = rows.Sum(r => r.Dms);
        var replies = rows.Sum(r => r.Replies);
        var conversions = rows.Sum(r => r.Conversions);
        return new FunnelDto(
            platformLabel,
            leads,
            dms,
            replies,
            conversions,
            Rate(dms, leads),
            Rate(replies, dms),
            Rate(conversions, leads));
    }

    public async Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var sessions = await _db.AgentSessions.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.StartedAt >= start && s.StartedAt < end)
            .Include(s => s.Traces)
            .ToListAsync(ct).ConfigureAwait(false);

        return sessions
            .GroupBy(s => new { s.AgentId, AgentName = ResolveAgentName(s) })
            .Select(g =>
            {
                var sessionsCount = g.Count();
                var completed = g.Count(s => s.Status == "completed");
                var quality = g
                    .SelectMany(s => s.Traces)
                    .Select(TryParseQualityTrace)
                    .Where(q => q is not null)
                    .Select(q => q!.Value)
                    .ToList();
                var qualityScores = quality
                    .Where(q => q.Score.HasValue)
                    .Select(q => q.Score!.Value)
                    .ToList();
                return new AgentPerformanceDto(
                    g.Key.AgentId,
                    g.Key.AgentName,
                    sessionsCount,
                    completed,
                    g.Sum(s => s.Traces.Count),
                    Rate(completed, sessionsCount),
                    quality.Count,
                    quality.Count(q => q.Passed),
                    Rate(quality.Count(q => q.Passed), quality.Count),
                    qualityScores.Count == 0 ? null : Math.Round(qualityScores.Average(), 4));
            })
            .OrderByDescending(r => r.Sessions)
            .ThenBy(r => r.AgentName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ForecastDto>> GetForecastAsync(
        Guid tenantId,
        string platform,
        string metric,
        int horizon,
        CancellationToken ct = default)
    {
        var normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? "all" : platform.Trim().ToLowerInvariant();
        var normalizedMetric = metric.Trim().ToLowerInvariant();
        var freshAfter = _clock.UtcNow.AddHours(-24);
        var rows = await _db.KpiForecasts.IgnoreQueryFilters()
            .Where(f =>
                f.TenantId == tenantId &&
                f.Platform == normalizedPlatform &&
                f.Metric == normalizedMetric &&
                f.GeneratedAt >= freshAfter)
            .OrderBy(f => f.ForecastDate)
            .Take(Math.Max(1, horizon))
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(f => new ForecastDto(
                f.ForecastDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                f.Platform,
                f.Metric,
                (double)f.Value,
                (double)f.LowerBound,
                (double)f.UpperBound))
            .ToList();
    }

    public async Task<IReadOnlyList<KpiDailyDto>> LoadKpiAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        string? platform,
        CancellationToken ct = default)
    {
        var query = _db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId && k.Date >= from && k.Date <= to);

        if (!string.IsNullOrWhiteSpace(platform))
        {
            var normalizedPlatform = platform.Trim().ToLowerInvariant();
            query = query.Where(k => k.Platform == normalizedPlatform);
        }

        return await query
            .OrderBy(k => k.Date)
            .ThenBy(k => k.Platform)
            .Select(k => new KpiDailyDto(
                k.Date,
                k.Platform,
                k.Leads,
                k.Dms,
                k.Replies,
                k.Conversions,
                k.AvgResponseTimeSec,
                k.AdSpend,
                k.AdSpend.HasValue && k.Leads > 0 ? k.AdSpend.Value / k.Leads : null))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public static IReadOnlyList<OmniChannelRowDto> BuildOmniRows(IEnumerable<KpiDailyDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(r => r.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var leads = g.Sum(r => r.Leads);
                var adSpend = SumNullable(g.Select(r => r.AdSpend));
                return new OmniChannelRowDto(
                    g.Key,
                    leads,
                    g.Sum(r => r.Dms),
                    g.Sum(r => r.Replies),
                    g.Sum(r => r.Conversions),
                    AverageNullable(g.Select(r => r.AvgResponseTimeSec)),
                    adSpend,
                    adSpend.HasValue && leads > 0 ? Math.Round(adSpend.Value / leads, 2) : null);
            })
            .OrderBy(r => PlatformOrder(r.Platform))
            .ThenBy(r => r.Platform, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsFreshForecast(DateTimeOffset generatedAt, DateTimeOffset now) =>
        generatedAt >= now.AddHours(-24);

    private static decimal Rate(int numerator, int denominator) =>
        denominator == 0 ? 0m : Math.Round((decimal)numerator / denominator, 4);

    private static string ResolveAgentName(AgentSession session) =>
        session.Traces
            .Select(t => t.AgentName)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
        ?? session.AgentId?.ToString()
        ?? "unassigned";

    private static QualityTrace? TryParseQualityTrace(AgentTrace trace)
    {
        if (!string.Equals(trace.Phase, "quality", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(trace.Message))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(trace.Message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("passed", out var passedElement) ||
                passedElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                return null;
            }

            decimal? score = null;
            if (root.TryGetProperty("score", out var scoreElement) &&
                scoreElement.ValueKind == JsonValueKind.Number &&
                scoreElement.TryGetDecimal(out var parsedScore))
            {
                score = parsedScore;
            }

            return new QualityTrace(passedElement.GetBoolean(), score);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct QualityTrace(bool Passed, decimal? Score);

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : Math.Round(present.Average(), 2);
    }

    private static decimal? SumNullable(IEnumerable<decimal?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Sum();
    }

    private static int PlatformOrder(string platform) =>
        platform.ToLowerInvariant() switch
        {
            "all" => 0,
            "zalo" => 1,
            "facebook" => 2,
            "instagram" => 3,
            "tiktok" => 4,
            "youtube" => 5,
            _ => 99,
        };
}
