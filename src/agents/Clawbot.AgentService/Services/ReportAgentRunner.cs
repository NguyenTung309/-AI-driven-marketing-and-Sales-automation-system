using System.Globalization;
using System.Text.Json;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
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
