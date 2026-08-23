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
        var rows = await _db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == tenantId && k.Date == metricDate)
            .OrderBy(k => k.Platform)
            .Select(k => new ReportSnapshotRow(
                k.Platform,
                k.Leads,
                k.Dms,
                k.Replies,
                k.Conversions,
                (double)(k.AvgResponseTimeSec ?? 0m)))
            .ToListAsync(ct).ConfigureAwait(false);

        return rows;
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
    /// Khoảng ngày cho báo cáo marketing: [date - (days-1), date]. Báo cáo nội dung tính theo khoảng
    /// chứ không theo một ngày như snapshot KPI — một ngày lẻ thường không có bài nào đăng.
    /// </summary>
    public static (DateOnly From, DateOnly To) ResolveRange(string? dateRaw, int? days, int defaultDays)
    {
        var to = ParseDate(dateRaw ?? string.Empty);
        var window = days is > 0 ? Math.Min(days.Value, MaxRangeDays) : defaultDays;
        return (to.AddDays(-(window - 1)), to);
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

        switch (raw.ToLowerInvariant())
        {
            case "today" or "hôm nay" or "hom nay":
                return Today();
            case "yesterday" or "hôm qua" or "hom qua":
                return Today().AddDays(-1);
            default:
                break;
        }

        if (DateOnly.TryParseExact(raw, AcceptedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new ArgumentException(string.Create(
            CultureInfo.InvariantCulture,
            $"date '{date}' is not a valid date. Use YYYY-MM-DD (e.g. {FormatDate(Today())}), or 'today'/'yesterday'."));
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
