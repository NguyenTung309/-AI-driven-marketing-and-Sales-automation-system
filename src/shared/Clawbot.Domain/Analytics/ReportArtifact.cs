using Clawbot.Domain.Common;

namespace Clawbot.Domain.Analytics;

/// <summary>Một cột trong bảng báo cáo. <paramref name="Type"/>: text | number | date.</summary>
public sealed record ReportColumn(string Key, string Label, string Type);

/// <summary>Trục X và các chuỗi số để vẽ biểu đồ. Null nghĩa là báo cáo chỉ có bảng.</summary>
public sealed record ReportChart(string X, IReadOnlyList<string> Series);

/// <summary>
/// Payload chuẩn hoá của mọi loại báo cáo (snapshot/anomaly/forecast) để một trang và một bộ
/// export dùng chung được, thay vì mỗi loại một shape riêng.
/// </summary>
public sealed record ReportArtifactPayload(
    string Kind,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    ReportChart? Chart);

/// <summary>
/// Kết quả một lần chạy report-agent, chốt lại thành bản bất biến để mở bằng link và xuất file.
/// Số liệu nằm trong <see cref="DataJson"/> và không tính lại khi xem — báo cáo đã gửi cho người
/// khác phải luôn hiện đúng con số lúc sinh ra.
/// </summary>
public sealed class ReportArtifact : Entity<Guid>, ITenantOwned
{
    public const string KindSnapshot = "snapshot";
    public const string KindAnomaly = "anomaly";
    public const string KindForecast = "forecast";

    public Guid TenantId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public string? Metric { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public string DataJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private ReportArtifact() { }

    public static ReportArtifact Create(
        Guid tenantId,
        string kind,
        string title,
        string platform,
        string? metric,
        DateOnly fromDate,
        DateOnly toDate,
        string dataJson,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Kind = kind,
            Title = title,
            Platform = platform,
            Metric = metric,
            FromDate = fromDate,
            ToDate = toDate,
            DataJson = dataJson,
            CreatedAt = createdAt,
        };
}
