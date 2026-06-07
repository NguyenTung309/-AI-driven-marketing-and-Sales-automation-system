namespace Clawbot.SharedKernel.Content;

public sealed record ContentTrendScanEvent(Guid TenantId, int TrendCount, DateTimeOffset OccurredAt);

public sealed record ContentPublishFailedEvent(
    Guid TenantId,
    Guid ContentItemId,
    Guid ScheduleId,
    string Platform,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record AnalyticsAlertEvent(
    Guid TenantId,
    string AlertType,
    string Platform,
    string Metric,
    string Severity,
    string Message,
    DateTimeOffset OccurredAt);

public interface IContentNotifier
{
    Task NotifyTrendScanAsync(Guid tenantId, ContentTrendScanEvent evt, CancellationToken ct = default);

    Task NotifyPublishFailedAsync(Guid tenantId, ContentPublishFailedEvent evt, CancellationToken ct = default);

    Task NotifyAnalyticsAlertAsync(Guid tenantId, AnalyticsAlertEvent evt, CancellationToken ct = default);
}
