using System.Globalization;
using System.Text.Json;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed record ReportSnapshotRow(
    string Platform,
    int Leads,
    int Dms,
    int Replies,
    int Conversions,
    double AvgResponseTimeSec);

public sealed record KpiDailyTrendRow(
    string Date,
    int Leads,
    int Dms,
    int Replies,
    int Conversions,
    double AvgResponseTimeSec);

public sealed record KpiRangeReport(
    DateOnly FromDate,
    DateOnly ToDate,
    string Platform,
    int TotalLeads,
    int TotalDms,
    int TotalReplies,
    int TotalConversions,
    double AvgResponseTimeSec,
    IReadOnlyList<ReportSnapshotRow> PlatformRows,
    IReadOnlyList<KpiDailyTrendRow> DailyTrends);

/// <summary>Hiệu suất nội dung đã đăng theo nền tảng — số liệu marketing, không đụng kpi_daily.</summary>
public sealed record ContentSnapshotRow(
    string Platform,
    int PostsPublished,
    int Likes,
    int Comments,
    int ReactionsTotal);

/// <summary>Phễu duyệt nội dung theo nền tảng: mỗi cột là một trạng thái quy trình đang tồn đọng.</summary>
public sealed record ContentFunnelRow(
    string Platform,
    int AwaitingAgentReview,
    int AgentReviewRunning,
    int AgentReviewNonPass,
    int ReviewFailed,
    int AwaitingHumanApproval,
    int ApprovedAwaitingSchedule,
    int Scheduled,
    int Published,
    int Rejected,
    int Total);

/// <summary>
/// Phễu kèm cờ báo đã chạm trần bản ghi. Cờ này phải đi tới tận câu trả lời của agent: một bảng bị
/// cắt bớt trong im lặng trông y hệt một bảng đầy đủ, và người đọc sẽ tin vào con số sai.
/// </summary>
public sealed record ContentFunnelReport(
    IReadOnlyList<ContentFunnelRow> Rows,
    bool Truncated,
    int Cap);

/// <summary>
/// Core report logic shared by <see cref="ReportAgentGrpcService"/> and the orchestration
/// report adapter. Validation throws plain exceptions (ArgumentException) so callers map them
/// to their own transport (gRPC status vs orchestration AgentResult.Error).
/// </summary>
public sealed class ReportAgentRunner(
    AppDbContext db,
    IAnomalyDetector anomalyDetector,
    IForecaster forecaster)
{
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db = db;
    private readonly IAnomalyDetector _anomalyDetector = anomalyDetector;
    private readonly IForecaster _forecaster = forecaster;

    public async Task<IReadOnlyList<ReportSnapshotRow>> DailySnapshotAsync(
        Guid tenantId, string dateRaw, CancellationToken ct)
    {
        var metricDate = ParseDate(dateRaw);
        var report = await KpiSnapshotAsync(tenantId, metricDate, metricDate, platform: null, ct).ConfigureAwait(false);
        return report.PlatformRows;
    }

    /// <summary>
    /// Tổng hợp KPI kinh doanh (Leads, DMs, Phản hồi, Chuyển đổi) theo ngày hoặc khoảng ngày (tuần/tháng).
    /// </summary>
    public async Task<KpiRangeReport> KpiSnapshotAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, string? platform, CancellationToken ct)
    {
        var normalizedPlatform = NormalizePlatformFilter(platform);

        var query = _db.KpiDailies.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(k => k.TenantId == tenantId && k.Date >= fromDate && k.Date <= toDate);

        if (normalizedPlatform is not null)
            query = query.Where(k => k.Platform == normalizedPlatform);

        var rawRows = await query.ToListAsync(ct).ConfigureAwait(false);
        if (rawRows.Count == 0)
        {
            rawRows = await AggregateLiveKpisAsync(tenantId, fromDate, toDate, normalizedPlatform, ct).ConfigureAwait(false);
        }

        // Trường hợp 1 ngày đơn lẻ và không lọc platform: giữ nguyên mọi dòng platform (facebook, zalo, all...)
        if (fromDate == toDate && normalizedPlatform is null)
        {
            var singleDayRows = rawRows
                .OrderBy(r => r.Platform, StringComparer.Ordinal)
                .Select(r => new ReportSnapshotRow(
                    r.Platform,
                    r.Leads,
                    r.Dms,
                    r.Replies,
                    r.Conversions,
                    (double)(r.AvgResponseTimeSec ?? 0m)))
                .ToList();

            var allRow = rawRows.FirstOrDefault(r => string.Equals(r.Platform, "all", StringComparison.OrdinalIgnoreCase));
            var nonAll = rawRows.Where(r => !string.Equals(r.Platform, "all", StringComparison.OrdinalIgnoreCase)).ToList();

            var totLeads = allRow?.Leads ?? (nonAll.Count > 0 ? nonAll.Sum(r => r.Leads) : 0);
            var totDms = allRow?.Dms ?? (nonAll.Count > 0 ? nonAll.Sum(r => r.Dms) : 0);
            var totReplies = allRow?.Replies ?? (nonAll.Count > 0 ? nonAll.Sum(r => r.Replies) : 0);
            var totConversions = allRow?.Conversions ?? (nonAll.Count > 0 ? nonAll.Sum(r => r.Conversions) : 0);
            var avgResp = allRow?.AvgResponseTimeSec.HasValue == true
                ? (double)allRow.AvgResponseTimeSec.Value
                : (nonAll.Count > 0 && nonAll.Any(r => r.AvgResponseTimeSec.HasValue && r.AvgResponseTimeSec.Value > 0)
                    ? Math.Round(nonAll.Where(r => r.AvgResponseTimeSec.HasValue && r.AvgResponseTimeSec.Value > 0).Average(r => (double)r.AvgResponseTimeSec!.Value), 1)
                    : 0d);

            var trends = new List<KpiDailyTrendRow>
            {
                new(FormatDate(fromDate), totLeads, totDms, totReplies, totConversions, avgResp)
            };

            return new KpiRangeReport(
                fromDate,
                toDate,
                "all",
                totLeads,
                totDms,
                totReplies,
                totConversions,
                avgResp,
                singleDayRows,
                trends);
        }

        // Trường hợp khoảng ngày (tuần/tháng/range) hoặc có lọc platform
        var nonAllPlatforms = rawRows.Where(r => !string.Equals(r.Platform, "all", StringComparison.OrdinalIgnoreCase)).ToList();
        var platformSource = (normalizedPlatform is null && nonAllPlatforms.Count > 0) ? nonAllPlatforms : rawRows;

        var platformRows = platformSource
            .GroupBy(r => PlatformKey(r.Platform), StringComparer.Ordinal)
            .Select(g =>
            {
                var responseTimes = g.Where(r => r.AvgResponseTimeSec.HasValue && r.AvgResponseTimeSec.Value > 0)
                                     .Select(r => (double)r.AvgResponseTimeSec!.Value)
                                     .ToList();
                var avgResp = responseTimes.Count > 0 ? Math.Round(responseTimes.Average(), 1) : 0d;

                return new ReportSnapshotRow(
                    g.Key,
                    g.Sum(r => r.Leads),
                    g.Sum(r => r.Dms),
                    g.Sum(r => r.Replies),
                    g.Sum(r => r.Conversions),
                    avgResp);
            })
            .OrderBy(r => r.Platform, StringComparer.Ordinal)
            .ToList();

        if (platformRows.Count == 0 && rawRows.Count > 0)
        {
            var avgResp = rawRows.Where(r => r.AvgResponseTimeSec.HasValue && r.AvgResponseTimeSec.Value > 0)
                                 .Select(r => (double)r.AvgResponseTimeSec!.Value)
                                 .DefaultIfEmpty(0d)
                                 .Average();
            platformRows = [new ReportSnapshotRow(
                normalizedPlatform ?? "all",
                rawRows.Sum(r => r.Leads),
                rawRows.Sum(r => r.Dms),
                rawRows.Sum(r => r.Replies),
                rawRows.Sum(r => r.Conversions),
                Math.Round(avgResp, 1))];
        }

        // Xu hướng theo từng ngày trong khoảng thời gian [fromDate, toDate]
        var dailyTrends = new List<KpiDailyTrendRow>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var targetDate = d;
            var dateRows = platformSource.Where(r => r.Date == targetDate).ToList();
            if (dateRows.Count == 0 && nonAllPlatforms.Count > 0)
            {
                dateRows = rawRows.Where(r => r.Date == targetDate).ToList();
            }

            var respList = dateRows.Where(r => r.AvgResponseTimeSec.HasValue && r.AvgResponseTimeSec.Value > 0)
                                   .Select(r => (double)r.AvgResponseTimeSec!.Value)
                                   .ToList();
            var avgResp = respList.Count > 0 ? Math.Round(respList.Average(), 1) : 0d;

            dailyTrends.Add(new KpiDailyTrendRow(
                FormatDate(targetDate),
                dateRows.Sum(r => r.Leads),
                dateRows.Sum(r => r.Dms),
                dateRows.Sum(r => r.Replies),
                dateRows.Sum(r => r.Conversions),
                avgResp));
        }

        var totalLeads = platformRows.Sum(r => r.Leads);
        var totalDms = platformRows.Sum(r => r.Dms);
        var totalReplies = platformRows.Sum(r => r.Replies);
        var totalConversions = platformRows.Sum(r => r.Conversions);
        var overallRespList = platformRows.Where(r => r.AvgResponseTimeSec > 0).Select(r => r.AvgResponseTimeSec).ToList();
        var overallAvgResp = overallRespList.Count > 0 ? Math.Round(overallRespList.Average(), 1) : 0d;

        return new KpiRangeReport(
            fromDate,
            toDate,
            normalizedPlatform ?? "all",
            totalLeads,
            totalDms,
            totalReplies,
            totalConversions,
            overallAvgResp,
            platformRows,
            dailyTrends);
    }

    /// <summary>
    /// Bài đã đăng thật (content_schedules.status = posted) kèm tương tác do MetaEngagementSyncJob
    /// đồng bộ về. Đây là nguồn marketing duy nhất có số liệu sống — content_workflow_metrics_hourly
    /// tuy có schema nhưng chưa có chỗ nào ghi, dùng nó sẽ ra báo cáo rỗng.
    /// </summary>
    public async Task<IReadOnlyList<ContentSnapshotRow>> ContentSnapshotAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, string platform, CancellationToken ct)
    {
        var (fromAt, toAt) = ToAnalyticsWindow(fromDate, toDate);
        var normalizedPlatform = NormalizePlatformFilter(platform);

        var query = _db.ContentSchedules.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                && s.Status == ContentSchedule.StatusPosted
                && s.PostedAt != null
                && s.PostedAt >= fromAt
                && s.PostedAt < toAt);

        if (normalizedPlatform is not null)
            query = query.Where(s => s.Platform == normalizedPlatform);

        // Gộp ngay dưới SQL: một khoảng 365 ngày có thể chứa hàng chục nghìn lịch đã đăng, nạp hết về
        // rồi mới cộng là truy vấn không chặn. Kết quả trả về tối đa vài dòng (một dòng mỗi nền tảng).
        var grouped = await query
            .GroupBy(s => s.Platform)
            .Select(g => new
            {
                Platform = g.Key,
                Posts = g.Count(),
                Likes = g.Sum(s => s.LikeCount ?? 0),
                Comments = g.Sum(s => s.CommentCount ?? 0),
                // ReactionsTotal chỉ Facebook mới có phân loại; Instagram chỉ có like nên lấy LikeCount
                // làm giá trị thay thế, để cột tổng cảm xúc không rỗng một cách khó hiểu.
                Reactions = g.Sum(s => s.ReactionsTotal ?? s.LikeCount ?? 0),
            })
            .ToListAsync(ct).ConfigureAwait(false);

        // Gộp lần hai trong bộ nhớ chỉ để hạ chữ khóa nền tảng: SQL Server và SQLite khác nhau về
        // phân biệt hoa thường nên "Facebook" và "facebook" có thể ra hai nhóm riêng.
        return grouped
            .GroupBy(r => PlatformKey(r.Platform), StringComparer.Ordinal)
            .Select(g => new ContentSnapshotRow(
                g.Key,
                g.Sum(r => r.Posts),
                g.Sum(r => r.Likes),
                g.Sum(r => r.Comments),
                g.Sum(r => r.Reactions)))
            .OrderBy(r => r.Platform, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Phễu duyệt nội dung: đếm bài theo trạng thái quy trình hiện tại.
    /// <para>
    /// Đây là ảnh chụp tồn đọng chứ không phải dòng chảy theo ngày, nên <paramref name="fromDate"/>
    /// mặc định là null (không chặn dưới): bài kẹt lâu nhất — thứ đáng báo cáo nhất — thường được tạo
    /// từ trước cửa sổ, lọc theo ngày tạo sẽ giấu đúng những bài đó đi.
    /// </para>
    /// <para>
    /// Nạp hẳn entity chứ không tính lại trạng thái bằng SQL vì <see cref="ContentItem.ResolveWorkflowState"/>
    /// là định nghĩa duy nhất — chép lại logic đó ra đây là cách chắc chắn nhất để hai nơi lệch nhau.
    /// </para>
    /// </summary>
    public async Task<ContentFunnelReport> ContentFunnelAsync(
        Guid tenantId, DateOnly? fromDate, DateOnly toDate, string platform, CancellationToken ct)
    {
        var (fromAt, toAt) = ToAnalyticsWindow(fromDate ?? toDate, toDate);
        var normalizedPlatform = NormalizePlatformFilter(platform);

        var query = _db.ContentItems.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId
                && i.DeletedAt == null
                && i.CreatedAt < toAt);

        if (fromDate is not null)
            query = query.Where(i => i.CreatedAt >= fromAt);

        if (normalizedPlatform is not null)
            query = query.Where(i => i.Platform == normalizedPlatform);

        // Lấy dư một bản ghi để biết có bị cắt hay không mà không phải chạy thêm một câu đếm.
        // Cắt bớt trong im lặng sẽ cho ra một bảng trông đầy đủ nhưng tổng sai.
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(MaxFunnelItems + 1)
            .ToListAsync(ct).ConfigureAwait(false);

        var truncated = items.Count > MaxFunnelItems;
        if (truncated)
            items = items.Take(MaxFunnelItems).ToList();

        var rows = items
            .GroupBy(i => PlatformKey(i.Platform), StringComparer.Ordinal)
            .Select(g =>
            {
                var states = g.Select(item => item.ResolveWorkflowState()).ToList();
                int CountOf(string state) => states.Count(s => string.Equals(s, state, StringComparison.Ordinal));
                return new ContentFunnelRow(
                    g.Key,
                    CountOf("awaiting_agent_review"),
                    CountOf("agent_review_running"),
                    CountOf("agent_review_non_pass"),
                    CountOf("review_failed"),
                    CountOf("awaiting_human_approval"),
                    CountOf("approved_awaiting_schedule"),
                    CountOf("scheduled"),
                    CountOf("published"),
                    CountOf("rejected"),
                    states.Count);
            })
            .OrderBy(r => r.Platform, StringComparer.Ordinal)
            .ToList();

        return new ContentFunnelReport(rows, truncated, MaxFunnelItems);
    }

    /// <summary>
    /// Khoảng ngày cho báo cáo KPI: hỗ trợ cả ngày lẻ (mặc định hôm nay), từ khóa tuần/tháng,
    /// khoảng [from, to], hoặc số ngày lookback_days.
    /// </summary>
    public static (DateOnly From, DateOnly To) ResolveKpiRange(
        string? dateRaw,
        string? fromRaw = null,
        string? toRaw = null,
        int? lookbackDays = null,
        int defaultDays = 1)
    {
        var today = Today();

        // 1. Nếu có cả from và to
        if (!string.IsNullOrWhiteSpace(fromRaw) && !string.IsNullOrWhiteSpace(toRaw))
        {
            var from = ParseDate(fromRaw);
            var to = ParseDate(toRaw);
            return from <= to ? (from, to) : (to, from);
        }

        if (!string.IsNullOrWhiteSpace(fromRaw))
        {
            var from = ParseDate(fromRaw);
            var to = lookbackDays is > 0 ? from.AddDays(lookbackDays.Value - 1) : today;
            return from <= to ? (from, to) : (to, from);
        }

        var raw = (dateRaw ?? string.Empty).Trim().ToLowerInvariant();

        // 2. Các từ khóa khoảng thời gian tương đối
        switch (raw)
        {
            case "this_week" or "thisweek" or "tuần này" or "tuan nay" or "tuannay" or "tuần hiện tại" or "tuan hien tai":
            {
                // Ở Việt Nam, tuần bắt đầu từ Thứ Hai (Monday)
                var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
                if (diff < 0) diff += 7; // Chủ Nhật (0) -> diff = 6
                var startOfWeek = today.AddDays(-diff);
                return (startOfWeek, today);
            }
            case "last_week" or "lastweek" or "tuần trước" or "tuan truoc" or "tuantruoc" or "tuần qua" or "tuan qua":
            {
                var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
                if (diff < 0) diff += 7;
                var endOfLastWeek = today.AddDays(-diff - 1);
                var startOfLastWeek = endOfLastWeek.AddDays(-6);
                return (startOfLastWeek, endOfLastWeek);
            }
            case "this_month" or "thismonth" or "tháng này" or "thang nay" or "thangnay" or "tháng hiện tại" or "thang hien tai":
            {
                var startOfMonth = new DateOnly(today.Year, today.Month, 1);
                return (startOfMonth, today);
            }
            case "last_month" or "lastmonth" or "tháng trước" or "thang truoc" or "thangtruoc":
            {
                var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
                var endOfLastMonth = firstOfThisMonth.AddDays(-1);
                var startOfLastMonth = new DateOnly(endOfLastMonth.Year, endOfLastMonth.Month, 1);
                return (startOfLastMonth, endOfLastMonth);
            }
            case "today" or "hôm nay" or "hom nay" or "homnay":
                return (today, today);
            case "yesterday" or "hôm qua" or "hom qua" or "homqua":
                return (today.AddDays(-1), today.AddDays(-1));
            default:
                break;
        }

        // 3. Nếu có lookback_days
        if (lookbackDays is > 0)
        {
            var to = string.IsNullOrWhiteSpace(dateRaw) ? today : ParseDate(dateRaw);
            var window = Math.Min(lookbackDays.Value, MaxRangeDays);
            return (to.AddDays(-(window - 1)), to);
        }

        // 4. Nếu có dateRaw dạng ngày cụ thể
        if (!string.IsNullOrWhiteSpace(dateRaw))
        {
            var parsed = ParseDate(dateRaw);
            if (defaultDays > 1)
                return (parsed.AddDays(-(defaultDays - 1)), parsed);
            return (parsed, parsed);
        }

        // 5. Mặc định
        if (defaultDays > 1)
            return (today.AddDays(-(defaultDays - 1)), today);

        return (today, today);
    }

    /// <summary>
    /// Khoảng ngày cho báo cáo marketing: [date - (days-1), date]. Báo cáo nội dung tính theo khoảng
    /// chứ không theo một ngày như snapshot KPI — một ngày lẻ thường không có bài nào đăng.
    /// </summary>
    public static (DateOnly From, DateOnly To) ResolveRange(string? dateRaw, int? days, int defaultDays)
    {
        var (from, to) = ResolveKpiRange(dateRaw, lookbackDays: days, defaultDays: defaultDays);
        return (from, to);
    }

    public async Task<IReadOnlyList<AnomalyPoint>> DetectAnomalyAsync(
        Guid tenantId, string platform, string metric, double zThreshold, int lookbackDays, CancellationToken ct)
    {
        var z = zThreshold > 0 ? zThreshold : 3d;
        var series = await LoadSeriesAsync(tenantId, platform, metric, lookbackDays > 0 ? lookbackDays : 30, ct)
            .ConfigureAwait(false);
        return await _anomalyDetector.ScoreAsync(series, z, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ForecastPoint>> ForecastAsync(
        Guid tenantId, string platform, string metric, int horizonDays, CancellationToken ct)
    {
        var horizon = horizonDays > 0 ? horizonDays : 7;
        var series = await LoadSeriesAsync(tenantId, platform, metric, lookbackDays: 90, ct).ConfigureAwait(false);
        return await _forecaster.ForecastAsync(series, horizon, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Chốt kết quả một lần chạy thành artifact bất biến và trả id để dựng link mở lại.
    /// camelCase bắt buộc: payload này đi thẳng xuống frontend, PascalCase sẽ khiến bảng rỗng.
    /// </summary>
    public async Task<Guid> SaveArtifactAsync(
        Guid tenantId,
        string kind,
        string title,
        string platform,
        string? metric,
        DateOnly fromDate,
        DateOnly toDate,
        ReportArtifactPayload payload,
        CancellationToken ct)
    {
        var artifact = ReportArtifact.Create(
            tenantId,
            kind,
            title,
            platform,
            metric,
            fromDate,
            toDate,
            JsonSerializer.Serialize(payload, ArtifactJsonOptions),
            DateTimeOffset.UtcNow);

        _db.ReportArtifacts.Add(artifact);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return artifact.Id;
    }

    public static string FormatDate(DateTimeOffset at) =>
        at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Hôm nay theo giờ VN. KpiAggregator gom dữ liệu theo mốc UTC+7 nên mọi chỗ suy ra "hôm nay"
    /// phải dùng chung mốc này — lấy ngày UTC sẽ lệch một ngày trong khoảng 00:00-07:00 giờ VN.
    /// </summary>
    public static DateOnly Today() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(AnalyticsOffset).DateTime);

    private async Task<List<KpiDaily>> AggregateLiveKpisAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, string? normalizedPlatform, CancellationToken ct)
    {
        var fromOffset = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), AnalyticsOffset);
        var toOffset = new DateTimeOffset(toDate.ToDateTime(TimeOnly.MaxValue), AnalyticsOffset);

        var leadQuery = _db.Leads.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.CreatedAt >= fromOffset && l.CreatedAt <= toOffset);

        var convQuery = _db.Conversations.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(c => c.Messages)
            .Where(c => c.TenantId == tenantId && ((c.CreatedAt >= fromOffset && c.CreatedAt <= toOffset) || c.Messages.Any(m => m.SentAt >= fromOffset && m.SentAt <= toOffset)));

        if (normalizedPlatform is not null && !string.Equals(normalizedPlatform, "all", StringComparison.OrdinalIgnoreCase))
        {
            leadQuery = leadQuery.Where(l => l.SourcePlatform == normalizedPlatform);
            convQuery = convQuery.Where(c => c.Platform == normalizedPlatform);
        }

        var leads = await leadQuery.ToListAsync(ct).ConfigureAwait(false);
        var conversations = await convQuery.ToListAsync(ct).ConfigureAwait(false);

        var result = new List<KpiDaily>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var dayStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), AnalyticsOffset);
            var dayEnd = dayStart.AddDays(1);

            var dayLeads = leads.Where(l => l.CreatedAt >= dayStart && l.CreatedAt < dayEnd).ToList();
            var dayConvs = conversations.Where(c => c.CreatedAt >= dayStart && c.CreatedAt < dayEnd).ToList();

            var platforms = dayLeads.Select(l => l.SourcePlatform?.Trim().ToLowerInvariant() ?? "unknown")
                .Concat(dayConvs.Select(c => c.Platform?.Trim().ToLowerInvariant() ?? "unknown"))
                .Distinct()
                .ToList();

            if (platforms.Count == 0)
                continue;

            foreach (var p in platforms)
            {
                var pLeads = dayLeads.Count(l => string.Equals(l.SourcePlatform, p, StringComparison.OrdinalIgnoreCase));
                var pConversions = dayLeads.Count(l => string.Equals(l.SourcePlatform, p, StringComparison.OrdinalIgnoreCase) && string.Equals(l.Stage, "customer", StringComparison.OrdinalIgnoreCase));
                var pDms = dayConvs.Count(c => string.Equals(c.Platform, p, StringComparison.OrdinalIgnoreCase));
                var pReplies = conversations
                    .Where(c => string.Equals(c.Platform, p, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(c => c.Messages)
                    .Count(m => m.Direction == "out" && m.SentAt >= dayStart && m.SentAt < dayEnd);

                var kpi = KpiDaily.Create(tenantId, d, p, DateTimeOffset.UtcNow);
                kpi.Record(pLeads, pDms, pReplies, pDms, pConversions, null);
                result.Add(kpi);
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<(DateTimeOffset At, double Value)>> LoadSeriesAsync(
        Guid tenantId, string platform, string metric, int lookbackDays, CancellationToken ct)
    {
        var normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? "all" : platform.Trim().ToLowerInvariant();
        var normalizedMetric = NormalizeMetric(metric);
        var rows = await _db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId && k.Platform == normalizedPlatform)
            .OrderByDescending(k => k.Date)
            .Take(Math.Max(1, lookbackDays))
            .OrderBy(k => k.Date)
            .ToListAsync(ct).ConfigureAwait(false);

        return rows
            .Select(row => new
            {
                At = new DateTimeOffset(row.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Value = MetricValue(row, normalizedMetric),
            })
            .Where(x => x.Value.HasValue)
            .Select(x => (x.At, x.Value!.Value))
            .ToList();
    }

    /// <summary>Metric hợp lệ duy nhất — dùng chung cho JSON Schema của tool và thông báo lỗi.</summary>
    public static readonly IReadOnlyList<string> SupportedMetrics =
    [
        "leads", "dms", "replies", "conversions", "avg_response_time_sec",
    ];

    /// <summary>Mốc giờ dùng để gom KPI — phải khớp KpiAggregator.AnalyticsOffset.</summary>
    private static readonly TimeSpan AnalyticsOffset = TimeSpan.FromHours(7);

    /// <summary>Trần cửa sổ ngày cho báo cáo nội dung — chặn goal kiểu "toàn bộ lịch sử" quét cả bảng.</summary>
    private const int MaxRangeDays = 365;

    /// <summary>Trần số bài nạp cho phễu; vượt ngưỡng thì lấy bài mới nhất trước.</summary>
    private const int MaxFunnelItems = 5000;

    /// <summary>
    /// null = không lọc (mọi nền tảng). Chuỗi rỗng/"all" đều mang nghĩa không lọc.
    /// So khớp phân biệt hoa thường như mọi truy vấn platform khác trong hệ thống: cột này luôn được
    /// ghi bằng mã nền tảng viết thường, nên chỉ cần hạ chữ ở đầu vào là đủ.
    /// </summary>
    private static string? NormalizePlatformFilter(string? platform)
    {
        var normalized = (platform ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 || normalized == "all" ? null : normalized;
    }

    private static string PlatformKey(string? platform) =>
        string.IsNullOrWhiteSpace(platform) ? "unknown" : platform.Trim().ToLowerInvariant();

    /// <summary>
    /// Biên thời gian của khoảng ngày theo giờ VN (UTC+7), nửa mở [from, to+1). Lấy biên theo UTC
    /// sẽ cắt mất bài đăng trong khoảng 00:00-07:00 giờ VN của ngày cuối.
    /// </summary>
    private static (DateTimeOffset From, DateTimeOffset To) ToAnalyticsWindow(DateOnly fromDate, DateOnly toDate) =>
        (new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), AnalyticsOffset),
            new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue), AnalyticsOffset));

    private static readonly string[] AcceptedDateFormats =
        ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"];

    // Ngày do LLM sinh ra nên lệch định dạng là chuyện thường: nhận thêm vài dạng phổ biến và từ khoá
    // tương đối thay vì bắt đúng một dạng rồi đốt một bước ReAct cho lỗi format.
    // KHÔNG dùng DateOnly.TryParse trần: "01/08/2026" sẽ thành 8 tháng 1 theo MM/dd của InvariantCulture,
    // tức trả sai ngày trong im lặng — tệ hơn hẳn báo lỗi.
    public static DateOnly ParseDate(string date)
    {
        var raw = (date ?? string.Empty).Trim();
        if (raw.Length == 0)
            return Today();

        var lower = raw.ToLowerInvariant();
        switch (lower)
        {
            case "today" or "hôm nay" or "hom nay" or "homnay":
                return Today();
            case "yesterday" or "hôm qua" or "hom qua" or "homqua":
                return Today().AddDays(-1);
            case "this_week" or "thisweek" or "tuần này" or "tuan nay" or "tuannay" or "tuần hiện tại" or "tuan hien tai":
            {
                var diff = (int)Today().DayOfWeek - (int)DayOfWeek.Monday;
                if (diff < 0) diff += 7;
                return Today().AddDays(-diff);
            }
            case "last_week" or "lastweek" or "tuần trước" or "tuan truoc" or "tuantruoc" or "tuần qua" or "tuan qua":
            {
                var diff = (int)Today().DayOfWeek - (int)DayOfWeek.Monday;
                if (diff < 0) diff += 7;
                return Today().AddDays(-diff - 7);
            }
            case "this_month" or "thismonth" or "tháng này" or "thang nay" or "thangnay" or "tháng hiện tại" or "thang hien tai":
                return new DateOnly(Today().Year, Today().Month, 1);
            case "last_month" or "lastmonth" or "tháng trước" or "thang truoc" or "thangtruoc":
            {
                var firstOfThisMonth = new DateOnly(Today().Year, Today().Month, 1);
                var endOfLastMonth = firstOfThisMonth.AddDays(-1);
                return new DateOnly(endOfLastMonth.Year, endOfLastMonth.Month, 1);
            }
            default:
                break;
        }

        if (DateOnly.TryParseExact(raw, AcceptedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new ArgumentException(string.Create(
            CultureInfo.InvariantCulture,
            $"date '{date}' is not a valid date. Use YYYY-MM-DD (e.g. {FormatDate(Today())}), or 'today'/'yesterday'/'this_week'/'this_month'."));
    }

    // Lỗi phải tự mô tả: ReAct loop chỉ có 5 bước nên "metric is not supported." đốt sạch ngân sách vì
    // model phải đoán mù. Liệt kê thẳng danh sách hợp lệ để nó sửa trong đúng một bước.
    private static string NormalizeMetric(string metric)
    {
        var normalized = (metric ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
        normalized = normalized switch
        {
            "response_time" or "avg_response_time" or "response_time_sec" => "avg_response_time_sec",
            "messages" or "dm" or "inbox" => "dms",
            "lead" or "leads_count" or "new_leads" => "leads",
            "conversion" or "orders" => "conversions",
            "reply" => "replies",
            _ => normalized,
        };

        if (!SupportedMetrics.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentException(string.Create(
                CultureInfo.InvariantCulture,
                $"metric '{metric}' is not supported. Supported metrics: {string.Join(", ", SupportedMetrics)}."));
        }

        return normalized;
    }

    private static double? MetricValue(KpiDaily row, string metric) =>
        metric switch
        {
            "leads" => row.Leads,
            "dms" => row.Dms,
            "replies" => row.Replies,
            "conversions" => row.Conversions,
            "avg_response_time_sec" => row.AvgResponseTimeSec.HasValue ? (double)row.AvgResponseTimeSec.Value : null,
            _ => null,
        };
}
