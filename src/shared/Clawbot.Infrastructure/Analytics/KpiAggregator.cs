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
            var allMessages = conversation.Messages
                .OrderBy(m => m.SentAt)
                .ToList();

            var replies = allMessages.Count(m => m.Direction == "out" && m.SentAt >= dayStart && m.SentAt < dayEnd);
            row.Replies += replies;
            aggregate.Replies += replies;

            DateTimeOffset? firstUnansweredInbound = null;
            foreach (var m in allMessages)
            {
                if (m.Direction == "in")
                {
                    // Chỉ lưu lại thời gian của tin nhắn đến ĐẦU TIÊN trong 1 lô tin nhắn liên tiếp
                    firstUnansweredInbound ??= m.SentAt;
                }
                else if (m.Direction == "out")
                {
                    if (firstUnansweredInbound != null)
                    {
                        // Thời gian phản hồi được tính vào báo cáo của ngày mà agent thực sự trả lời
                        if (m.SentAt >= dayStart && m.SentAt < dayEnd)
                        {
                            var seconds = (decimal)(m.SentAt - firstUnansweredInbound.Value).TotalSeconds;
                            row.ResponseSeconds.Add(seconds);
                            aggregate.ResponseSeconds.Add(seconds);
                        }
                        
                        // Đặt lại để chờ lượt chat (session) tiếp theo
                        firstUnansweredInbound = null;
                    }
                }
            }
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

        public KpiAggregateRow ToImmutable() =>
            new(
                Platform,
                Leads,
                Dms,
                Replies,
                RepliedDms,
                Conversions,
                ResponseSeconds.Count == 0 ? null : Math.Round(ResponseSeconds.Average(), 2));
    }
}
