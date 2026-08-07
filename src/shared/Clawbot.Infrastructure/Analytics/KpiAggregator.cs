using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Analytics;

public sealed class KpiAggregator(AppDbContext db) : IKpiAggregator
{
    public const string AggregatePlatform = "all";

    private static readonly TimeSpan AnalyticsOffset = TimeSpan.FromHours(7);

    private static readonly string[] SupportedPlatforms =
        ["facebook", "zalo", "instagram", "tiktok", "youtube"];

    private readonly AppDbContext _db = db;

    public async Task<IReadOnlyList<KpiAggregateRow>> AggregateDailyAsync(
        Guid tenantId,
        DateOnly metricDate,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId required", nameof(tenantId));

        var dayStart = new DateTimeOffset(metricDate.ToDateTime(TimeOnly.MinValue), AnalyticsOffset);
        var dayEnd = dayStart.AddDays(1);
        var rows = SupportedPlatforms.ToDictionary(p => p, p => new MutableKpiRow(p), StringComparer.OrdinalIgnoreCase);
        var aggregate = new MutableKpiRow(AggregatePlatform);
        var useClientDateFiltering = _db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        MutableKpiRow PlatformRow(string? platform)
        {
            var key = string.IsNullOrWhiteSpace(platform) ? "unknown" : platform;
            if (!rows.TryGetValue(key, out var row))
            {
                row = new MutableKpiRow(key);
                rows[key] = row;
            }

            return row;
        }

        var leadQuery = _db.Leads.IgnoreQueryFilters().Where(l => l.TenantId == tenantId);
        if (!useClientDateFiltering)
            leadQuery = leadQuery.Where(l => l.CreatedAt >= dayStart && l.CreatedAt < dayEnd);

        var tenantLeads = await leadQuery.ToListAsync(ct).ConfigureAwait(false);

        var leads = tenantLeads
            .Where(l => !useClientDateFiltering || (l.CreatedAt >= dayStart && l.CreatedAt < dayEnd))
            .GroupBy(l => l.SourcePlatform ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Platform = g.Key,
                Leads = g.Count(),
                Conversions = g.Count(l => l.Stage == "customer"),
            })
            .ToList();

        foreach (var lead in leads)
        {
            var row = PlatformRow(lead.Platform);
            row.Leads += lead.Leads;
            row.Conversions += lead.Conversions;
            aggregate.Leads += lead.Leads;
            aggregate.Conversions += lead.Conversions;
        }

        var conversationQuery = _db.Conversations.IgnoreQueryFilters().Where(c => c.TenantId == tenantId);
        if (!useClientDateFiltering)
        {
            conversationQuery = conversationQuery.Where(c =>
                (c.CreatedAt >= dayStart && c.CreatedAt < dayEnd) ||
                c.Messages.Any(m => m.SentAt >= dayStart && m.SentAt < dayEnd));
        }

        var conversations = await conversationQuery
            .Include(c => c.Messages)
            .AsSplitQuery()
            .ToListAsync(ct).ConfigureAwait(false);

        var dmsConversations = conversations
            .Where(c => c.CreatedAt >= dayStart && c.CreatedAt < dayEnd)
            .ToList();

        var dms = dmsConversations
            .GroupBy(c => c.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Platform = g.Key, Dms = g.Count() })
            .ToList();

        foreach (var dm in dms)
        {
            var row = PlatformRow(dm.Platform);
            row.Dms += dm.Dms;
            aggregate.Dms += dm.Dms;
        }

        // RepliedDms dem theo hoi thoai (chi tinh tren dmsConversations, cung tap voi Dms o tren) de
        // ti le tu dong hoa = RepliedDms / Dms khong bao gio vuot 100% du 1 hoi thoai co nhieu luot reply.
        var repliedDms = dmsConversations
            .Where(c => c.Messages.Any(m => m.Direction == "out" && m.SentAt >= dayStart && m.SentAt < dayEnd))
            .GroupBy(c => c.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Platform = g.Key, RepliedDms = g.Count() })
            .ToList();

        foreach (var rd in repliedDms)
        {
            var row = PlatformRow(rd.Platform);
            row.RepliedDms += rd.RepliedDms;
            aggregate.RepliedDms += rd.RepliedDms;
        }

        foreach (var conversation in conversations.Where(c => c.Messages.Any(m => m.SentAt >= dayStart && m.SentAt < dayEnd)))
        {
            var row = PlatformRow(conversation.Platform);
            var messages = conversation.Messages
                .Where(m => m.SentAt >= dayStart && m.SentAt < dayEnd)
                .OrderBy(m => m.SentAt)
                .ToList();

            var replies = messages.Count(m => m.Direction == "out");
            row.Replies += replies;
            aggregate.Replies += replies;

            foreach (var inbound in messages.Where(m => m.Direction == "in"))
            {
                var outbound = messages.FirstOrDefault(m => m.Direction == "out" && m.SentAt > inbound.SentAt);
                if (outbound is null)
                    continue;

                var seconds = (decimal)(outbound.SentAt - inbound.SentAt).TotalSeconds;
                row.ResponseSeconds.Add(seconds);
                aggregate.ResponseSeconds.Add(seconds);
            }
        }

        var adSpend = await _db.AdsCampaigns.IgnoreQueryFilters()
            .Join(
                _db.AdsMetricsDailies.IgnoreQueryFilters().Where(m => m.TenantId == tenantId && m.MetricDate == metricDate),
                c => c.Id,
                m => m.CampaignId,
                (c, m) => new { c.TenantId, c.Platform, m.Spend })
            .Where(x => x.TenantId == tenantId && x.Spend != null)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var spend in adSpend
            .GroupBy(x => x.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Platform = g.Key, Spend = g.Sum(x => x.Spend) }))
        {
            var row = PlatformRow(spend.Platform);
            row.AdSpend = (row.AdSpend ?? 0m) + spend.Spend;
            aggregate.AdSpend = (aggregate.AdSpend ?? 0m) + spend.Spend;
        }

        // Doanh thu approved theo ngày decided_at (không phải created_at) — join lead để lấy source_platform.
        var revenueQuery = _db.LeadRevenues.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId
                && r.Status == LeadRevenue.StatusApproved
                && r.DecidedAt != null);
        if (!useClientDateFiltering)
            revenueQuery = revenueQuery.Where(r => r.DecidedAt >= dayStart && r.DecidedAt < dayEnd);

        var revenueRows = await revenueQuery
            .Join(
                _db.Leads.IgnoreQueryFilters().Where(l => l.TenantId == tenantId),
                r => r.LeadId,
                l => l.Id,
                (r, l) => new { r.Amount, r.DecidedAt, Platform = l.SourcePlatform })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var rev in revenueRows
            .Where(r => !useClientDateFiltering || (r.DecidedAt >= dayStart && r.DecidedAt < dayEnd))
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Platform) ? "unknown" : r.Platform!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Platform = g.Key, Amount = g.Sum(x => x.Amount) }))
        {
            var row = PlatformRow(rev.Platform);
            row.Revenue = (row.Revenue ?? 0m) + rev.Amount;
            aggregate.Revenue = (aggregate.Revenue ?? 0m) + rev.Amount;
        }

        return rows.Values
            .OrderBy(r => SupportedPlatformIndex(r.Platform))
            .ThenBy(r => r.Platform, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.ToImmutable())
            .Append(aggregate.ToImmutable())
            .ToList();
    }

    private static int SupportedPlatformIndex(string platform)
    {
        var index = Array.FindIndex(SupportedPlatforms, p => string.Equals(p, platform, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private sealed class MutableKpiRow(string platform)
    {
        public string Platform { get; } = platform;
        public int Leads { get; set; }
        public int Dms { get; set; }
        public int Replies { get; set; }
        public int RepliedDms { get; set; }
        public int Conversions { get; set; }
        public List<decimal> ResponseSeconds { get; } = [];
        public decimal? AdSpend { get; set; }
        public decimal? Revenue { get; set; }

        public KpiAggregateRow ToImmutable() =>
            new(
                Platform,
                Leads,
                Dms,
                Replies,
                RepliedDms,
                Conversions,
                ResponseSeconds.Count == 0 ? null : Math.Round(ResponseSeconds.Average(), 2),
                AdSpend,
                Revenue);
    }
}
