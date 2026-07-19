using Clawbot.Domain.Common;

namespace Clawbot.Domain.Jobs;

public static class BackgroundJobStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string status) =>
        status is Succeeded or Failed or Cancelled;
}

/// <summary>
/// Run record cho một tác vụ chạy ngầm (sinh nội dung, sinh tài liệu, chạy test KB...).
/// Là nơi user click vào từ thông báo để xem trạng thái. Terminal state không quay ngược.
/// </summary>
public sealed class BackgroundJob : Entity<Guid>, ITenantOwned, IAuditExempt
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Status { get; private set; } = BackgroundJobStatuses.Queued;
    public int Progress { get; private set; }
    public string? ProgressNote { get; private set; }
    public string? PayloadJson { get; private set; }
    public string? ResultLink { get; private set; }
    public string? ResultSummary { get; private set; }
    public string? Error { get; private set; }
    public string? HangfireJobId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool CancelRequested { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private BackgroundJob() { }

    public static BackgroundJob Queue(
        Guid tenantId,
        Guid? userId,
        string type,
        string title,
        string? payloadJson,
        DateTimeOffset createdAt,
        string? idempotencyKey = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            Title = title,
            Status = BackgroundJobStatuses.Queued,
            PayloadJson = payloadJson,
            IdempotencyKey = idempotencyKey,
            CreatedAt = createdAt,
        };

    public void AttachHangfireJob(string hangfireJobId) => HangfireJobId = hangfireJobId;

    public void MarkRunning(DateTimeOffset at)
    {
        if (BackgroundJobStatuses.IsTerminal(Status)) return;
        Status = BackgroundJobStatuses.Running;
        StartedAt ??= at;
    }

    public void ReportProgress(int percent, string? note)
    {
        if (BackgroundJobStatuses.IsTerminal(Status)) return;
        Progress = Math.Clamp(percent, 0, 100);
        ProgressNote = note;
    }

    public void MarkSucceeded(string? resultLink, string? resultSummary, DateTimeOffset at)
    {
        if (BackgroundJobStatuses.IsTerminal(Status)) return;
        Status = BackgroundJobStatuses.Succeeded;
        Progress = 100;
        ResultLink = resultLink;
        ResultSummary = resultSummary;
        FinishedAt = at;
    }

    /// <param name="error">Message an toàn cho người đọc — đã redact, không stack trace.</param>
    public void MarkFailed(string error, DateTimeOffset at)
    {
        if (BackgroundJobStatuses.IsTerminal(Status)) return;
        Status = BackgroundJobStatuses.Failed;
        Error = error;
        FinishedAt = at;
    }

    /// <summary>Queued thì huỷ ngay; running thì chỉ đặt cờ — handler tự dừng qua CancellationToken.</summary>
    public void RequestCancel(DateTimeOffset at)
    {
        if (BackgroundJobStatuses.IsTerminal(Status)) return;
        CancelRequested = true;
        if (Status == BackgroundJobStatuses.Queued)
        {
            Status = BackgroundJobStatuses.Cancelled;
            FinishedAt = at;
        }
    }

    public void MarkCancelled(DateTimeOffset at)
    {
        if (BackgroundJobStatuses.IsTerminal(Status)) return;
        Status = BackgroundJobStatuses.Cancelled;
        FinishedAt = at;
    }

    /// <summary>
    /// Chạy lại job đã fail/huỷ trên chính row cũ — không tạo row mới, không phải serialize lại payload
    /// (payload đã redact PII, tái tạo từ nó là sai dữ liệu).
    /// </summary>
    public bool Requeue()
    {
        if (Status is not (BackgroundJobStatuses.Failed or BackgroundJobStatuses.Cancelled)) return false;
        Status = BackgroundJobStatuses.Queued;
        CancelRequested = false;
        Progress = 0;
        ProgressNote = null;
        Error = null;
        StartedAt = null;
        FinishedAt = null;
        return true;
    }
}
