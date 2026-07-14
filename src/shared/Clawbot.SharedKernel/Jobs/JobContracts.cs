namespace Clawbot.SharedKernel.Jobs;

/// <summary>Ngữ cảnh 1 lần chạy job. TenantId truyền tường minh — Hangfire không có HTTP scope để ITenantAccessor đọc.</summary>
public sealed record JobContext(
    Guid JobId,
    Guid TenantId,
    Guid? UserId,
    string PayloadJson,
    IJobProgress Progress);

/// <param name="ResultLink">Deep link tới trang nghiệp vụ xem kết quả; null = notification rơi về dialog job.</param>
public sealed record JobResult(string? ResultLink = null, string? Summary = null);

public interface IJobProgress
{
    Task ReportAsync(int percent, string? note, CancellationToken ct = default);
}

/// <summary>Một loại tác vụ nền. Type phải khớp <c>background_jobs.type</c> và là duy nhất.</summary>
public interface IJobHandler
{
    string Type { get; }

    /// <summary>
    /// false = xong thì KHÔNG bắn thông báo. Dành cho việc tương tác: user đang ngồi nhìn màn hình
    /// chờ kết quả (soạn câu trả lời, chạy thử agent) — rung chuông mỗi lần là spam.
    /// Việc vẫn nằm trong "Việc đang chạy" (thấy được, huỷ được) và LỖI thì vẫn luôn báo.
    /// </summary>
    bool NotifyOnSuccess => true;

    Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct);
}

/// <summary>
/// Đưa việc vào hàng đợi nền. CHỈ resolve được ở host API — Hangfire client chỉ cấu hình ở đó.
/// </summary>
public interface IJobLauncher
{
    /// <param name="userId">Người kích — để thông báo bắn đúng người. Null = job hệ thống (broadcast tenant).</param>
    Task<Guid> LaunchAsync(
        string type,
        string title,
        object? payload = null,
        Guid? userId = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
}

/// <summary>Push trạng thái job realtime. Implement ở tầng API (SignalR) — Infrastructure không tham chiếu SignalR.</summary>
public interface IJobRealtime
{
    Task JobUpdatedAsync(Guid tenantId, Guid? userId, object payload, CancellationToken ct = default);
}
