using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Notifications;

namespace Clawbot.Infrastructure.Notifications;

public sealed class PublishingContentNotifier(
    IContentNotifier realtime,
    INotificationPublisher publisher) : IContentNotifier
{
    public async Task NotifyTrendScanAsync(Guid tenantId, ContentTrendScanEvent evt, CancellationToken ct = default)
    {
        await realtime.NotifyTrendScanAsync(tenantId, evt, ct).ConfigureAwait(false);
    }

    public async Task NotifyPublishFailedAsync(Guid tenantId, ContentPublishFailedEvent evt, CancellationToken ct = default)
    {
        await realtime.NotifyPublishFailedAsync(tenantId, evt, ct).ConfigureAwait(false);
        await publisher.PublishAsync(new NotificationRequest(
            TenantId: tenantId,
            UserId: null,
            Type: "content_publish_failed",
            Title: $"Content publish failed on {evt.Platform}",
            Severity: "warning",
            Body: evt.Reason,
            Link: "/content"), ct).ConfigureAwait(false);
    }

    public async Task NotifyAnalyticsAlertAsync(Guid tenantId, AnalyticsAlertEvent evt, CancellationToken ct = default)
    {
        await realtime.NotifyAnalyticsAlertAsync(tenantId, evt, ct).ConfigureAwait(false);
        await publisher.PublishAsync(new NotificationRequest(
            TenantId: tenantId,
            UserId: null,
            Type: evt.AlertType,
            Title: $"Analytics alert: {evt.Metric}",
            Severity: evt.Severity,
            Body: evt.Message,
            Link: "/analytics"), ct).ConfigureAwait(false);
    }
}
