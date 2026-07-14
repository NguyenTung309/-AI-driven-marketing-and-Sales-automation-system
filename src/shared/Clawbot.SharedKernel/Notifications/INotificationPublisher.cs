namespace Clawbot.SharedKernel.Notifications;

/// <summary>Raise an alert: persist it + push realtime. UserId null = tenant broadcast.</summary>
/// <param name="GroupKey">
/// Khoá gom nhóm. Cùng khoá + chưa đọc + trong 24h thì cộng dồn vào 1 thông báo (occurrenceCount)
/// thay vì đẻ dòng mới — dùng cho việc máy móc lặp lại (đổi giá thầu, auto-reply, job fail).
/// Null = thông báo lẻ.
/// </param>
public sealed record NotificationRequest(
    Guid TenantId,
    Guid? UserId,
    string Type,
    string Title,
    string Severity = "info",
    string? Body = null,
    string? Link = null,
    string? GroupKey = null);

public interface INotificationPublisher
{
    Task PublishAsync(NotificationRequest request, CancellationToken ct = default);
}
