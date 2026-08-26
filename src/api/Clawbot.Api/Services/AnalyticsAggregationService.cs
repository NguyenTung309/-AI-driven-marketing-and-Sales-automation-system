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
        LeadScope? scope = null,
        CancellationToken ct = default)
    {
        var rows = await LoadKpiAsync(tenantId, from, to, platform: null, scope, ct).ConfigureAwait(false);
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
        LeadScope? scope = null,
        CancellationToken ct = default)
    {
        var normalized = string.Equals(compare, "wow", StringComparison.OrdinalIgnoreCase) ? "wow" : "dod";
        var shiftDays = normalized == "wow" ? 7 : Math.Max(1, to.DayNumber - from.DayNumber + 1);
        var prevFrom = from.AddDays(-shiftDays);
        var prevTo = to.AddDays(-shiftDays);

        var current = await LoadKpiAsync(tenantId, from, to, platform: null, scope, ct).ConfigureAwait(false);
        var previous = await LoadKpiAsync(tenantId, prevFrom, prevTo, platform: null, scope, ct).ConfigureAwait(false);

        var metrics = new List<MetricDeltaDto>
        {
            BuildDelta("leads", current.Sum(r => r.Leads), previous.Sum(r => r.Leads)),
            BuildDelta("dms", current.Sum(r => r.Dms), previous.Sum(r => r.Dms)),
            BuildDelta("replies", current.Sum(r => r.Replies), previous.Sum(r => r.Replies)),
            BuildDelta("repliedDms", current.Sum(r => r.RepliedDms), previous.Sum(r => r.RepliedDms)),
            BuildDelta("conversions", current.Sum(r => r.Conversions), previous.Sum(r => r.Conversions)),
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
        LeadScope? scope = null,
        CancellationToken ct = default)
    {
        var rows = await LoadKpiAsync(tenantId, from, to, platform, scope, ct).ConfigureAwait(false);
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
        LeadScope? scope = null,
        CancellationToken ct = default)
    {
        if (scope is not null && !scope.Unrestricted)
        {
            return Array.Empty<ForecastDto>();
        }

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
        LeadScope? scope = null,
        CancellationToken ct = default)
    {
        if (scope is not null && !scope.Unrestricted)
        {
            return await LoadScopedKpiAsync(tenantId, from, to, platform, scope, ct).ConfigureAwait(false);
        }

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
                k.RepliedDms,
                k.Conversions,
                k.AvgResponseTimeSec))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<KpiDailyDto>> LoadScopedKpiAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        string? platform,
        LeadScope scope,
        CancellationToken ct)
    {
        var offset = TimeSpan.FromHours(7);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), offset);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), offset);

        // 1. Leads
        var leadQuery = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.DeletedAt == null && l.CreatedAt >= start && l.CreatedAt < end)
            .ApplyLeadScope(scope, _db);

        var leadsList = await leadQuery
            .Select(l => new { l.CreatedAt, l.SourcePlatform, l.Stage })
            .ToListAsync(ct).ConfigureAwait(false);

        // 2. Conversations
        var inboxIds = scope.InboxIds.ToList();
        var conversationQuery = _db.Conversations.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.InboxId != null && inboxIds.Contains(c.InboxId.Value));

        var conversations = await conversationQuery
            .Where(c => (c.CreatedAt >= start && c.CreatedAt < end) || c.Messages.Any(m => m.SentAt >= start && m.SentAt < end))
            .Include(c => c.Messages)
            .AsSplitQuery()
            .ToListAsync(ct).ConfigureAwait(false);

        var result = new List<KpiDailyDto>();
        var normalizedPlatformFilter = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim().ToLowerInvariant();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset);
            var dayEnd = dayStart.AddDays(1);

            var dayLeads = leadsList.Where(l => l.CreatedAt >= dayStart && l.CreatedAt < dayEnd).ToList();
            var dayDmsConversations = conversations.Where(c => c.CreatedAt >= dayStart && c.CreatedAt < dayEnd).ToList();
            var dayActiveConversations = conversations.Where(c => c.Messages.Any(m => m.SentAt >= dayStart && m.SentAt < dayEnd)).ToList();

            var platforms = dayLeads.Select(l => NormalizePlatform(l.SourcePlatform))
                .Concat(dayDmsConversations.Select(c => NormalizePlatform(c.Platform)))
                .Concat(dayActiveConversations.Select(c => NormalizePlatform(c.Platform)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var p in platforms)
            {
                if (normalizedPlatformFilter != null && !string.Equals(p, normalizedPlatformFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pLeads = dayLeads.Where(l => string.Equals(NormalizePlatform(l.SourcePlatform), p, StringComparison.OrdinalIgnoreCase)).ToList();
                var pDms = dayDmsConversations.Where(c => string.Equals(NormalizePlatform(c.Platform), p, StringComparison.OrdinalIgnoreCase)).ToList();
                var pActive = dayActiveConversations.Where(c => string.Equals(NormalizePlatform(c.Platform), p, StringComparison.OrdinalIgnoreCase)).ToList();

                var leadCount = pLeads.Count;
                var conversions = pLeads.Count(l => string.Equals(l.Stage, "customer", StringComparison.OrdinalIgnoreCase));
                var dmCount = pDms.Count;
                var repliedDmCount = pDms.Count(c => c.Messages.Any(m => m.Direction == "out" && m.SentAt >= dayStart && m.SentAt < dayEnd));

                var replyCount = 0;
                var responseTimes = new List<decimal>();

                foreach (var c in pActive)
                {
                    var msgs = c.Messages.OrderBy(m => m.SentAt).ToList();
                    replyCount += msgs.Count(m => string.Equals(m.Direction, "out", StringComparison.OrdinalIgnoreCase) && m.SentAt >= dayStart && m.SentAt < dayEnd);

                    DateTimeOffset? firstUnansweredInbound = null;
                    foreach (var m in msgs)
                    {
                        if (string.Equals(m.Direction, "in", StringComparison.OrdinalIgnoreCase))
                        {
                            firstUnansweredInbound ??= m.SentAt;
                        }
                        else if (string.Equals(m.Direction, "out", StringComparison.OrdinalIgnoreCase))
                        {
                            if (firstUnansweredInbound != null)
                            {
                                if (m.SentAt >= dayStart && m.SentAt < dayEnd)
                                {
                                    var seconds = (decimal)(m.SentAt - firstUnansweredInbound.Value).TotalSeconds;
                                    if (seconds >= 0)
                                    {
                                        responseTimes.Add(seconds);
                                    }
                                }
                                firstUnansweredInbound = null;
                            }
                        }
                    }
                }

                decimal? avgResponseTime = responseTimes.Count > 0 ? Math.Round(responseTimes.Average(), 2) : null;

                result.Add(new KpiDailyDto(
                    date,
                    p,
                    leadCount,
                    dmCount,
                    replyCount,
                    repliedDmCount,
                    conversions,
                    avgResponseTime));
            }
        }

        return result;
    }

    private static string NormalizePlatform(string? platform) =>
        string.IsNullOrWhiteSpace(platform) ? "unknown" : platform.Trim().ToLowerInvariant();

    public static IReadOnlyList<OmniChannelRowDto> BuildOmniRows(IEnumerable<KpiDailyDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(r => r.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                return new OmniChannelRowDto(
                    g.Key,
                    g.Sum(r => r.Leads),
                    g.Sum(r => r.Dms),
                    g.Sum(r => r.Replies),
                    g.Sum(r => r.RepliedDms),
                    g.Sum(r => r.Conversions),
                    AverageNullable(g.Select(r => r.AvgResponseTimeSec)));
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

    private static int PlatformOrder(string platform) =>
        platform.ToLowerInvariant() switch
        {
            "all" => 0,
            "facebook" => 1,
            "zalo" => 2,
            "instagram" => 3,
            "tiktok" => 4,
            "youtube" => 5,
            _ => 99,
        };
}
