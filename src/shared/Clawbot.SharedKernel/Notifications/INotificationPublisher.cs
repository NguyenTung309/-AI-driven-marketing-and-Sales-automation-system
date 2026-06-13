namespace Clawbot.SharedKernel.Notifications;

/// <summary>Raise an alert: persist it + push realtime. UserId null = tenant broadcast.</summary>
public sealed record NotificationRequest(
    Guid TenantId,
    Guid? UserId,
    string Type,
    string Title,
    string Severity = "info",
    string? Body = null,
    string? Link = null);

public interface INotificationPublisher
{
    Task PublishAsync(NotificationRequest request, CancellationToken ct = default);
}
