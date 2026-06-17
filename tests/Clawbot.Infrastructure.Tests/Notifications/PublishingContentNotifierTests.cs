using Clawbot.Infrastructure.Notifications;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Notifications;
using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Notifications;

public sealed class PublishingContentNotifierTests
{
    [Fact]
    public async Task NotifyPublishFailedAsync_delegates_realtime_and_persists_notification()
    {
        var tenantId = Guid.NewGuid();
        var realtime = new RecordingContentNotifier();
        var publisher = new RecordingNotificationPublisher();
        var sut = new PublishingContentNotifier(realtime, publisher);
        var evt = new ContentPublishFailedEvent(
            tenantId,
            ContentItemId: Guid.NewGuid(),
            ScheduleId: Guid.NewGuid(),
            Platform: "facebook",
            Reason: "Graph token expired",
            OccurredAt: new DateTimeOffset(2026, 6, 17, 8, 30, 0, TimeSpan.Zero));

        await sut.NotifyPublishFailedAsync(tenantId, evt);

        realtime.PublishFailures.Should().ContainSingle().Which.evt.Should().Be(evt);
        var request = publisher.Requests.Should().ContainSingle().Which;
        request.TenantId.Should().Be(tenantId);
        request.Type.Should().Be("content_publish_failed");
        request.Severity.Should().Be("warning");
        request.Title.Should().Contain("facebook");
        request.Body.Should().Contain("Graph token expired");
        request.Link.Should().Be("/content");
    }

    [Fact]
    public async Task NotifyAnalyticsAlertAsync_delegates_realtime_and_persists_notification()
    {
        var tenantId = Guid.NewGuid();
        var realtime = new RecordingContentNotifier();
        var publisher = new RecordingNotificationPublisher();
        var sut = new PublishingContentNotifier(realtime, publisher);
        var evt = new AnalyticsAlertEvent(
            tenantId,
            AlertType: "kb_accuracy",
            Platform: "kb",
            Metric: "HSK1",
            Severity: "warning",
            Message: "KB HSK1 accuracy is below 85%.",
            OccurredAt: new DateTimeOffset(2026, 6, 17, 8, 45, 0, TimeSpan.Zero));

        await sut.NotifyAnalyticsAlertAsync(tenantId, evt);

        realtime.AnalyticsAlerts.Should().ContainSingle().Which.evt.Should().Be(evt);
        var request = publisher.Requests.Should().ContainSingle().Which;
        request.TenantId.Should().Be(tenantId);
        request.Type.Should().Be("kb_accuracy");
        request.Severity.Should().Be("warning");
        request.Title.Should().Contain("HSK1");
        request.Body.Should().Be("KB HSK1 accuracy is below 85%.");
        request.Link.Should().Be("/analytics");
    }

    private sealed class RecordingContentNotifier : IContentNotifier
    {
        public List<(Guid tenantId, ContentTrendScanEvent evt)> TrendScans { get; } = [];
        public List<(Guid tenantId, ContentPublishFailedEvent evt)> PublishFailures { get; } = [];
        public List<(Guid tenantId, AnalyticsAlertEvent evt)> AnalyticsAlerts { get; } = [];

        public Task NotifyTrendScanAsync(Guid tenantId, ContentTrendScanEvent evt, CancellationToken ct = default)
        {
            TrendScans.Add((tenantId, evt));
            return Task.CompletedTask;
        }

        public Task NotifyPublishFailedAsync(Guid tenantId, ContentPublishFailedEvent evt, CancellationToken ct = default)
        {
            PublishFailures.Add((tenantId, evt));
            return Task.CompletedTask;
        }

        public Task NotifyAnalyticsAlertAsync(Guid tenantId, AnalyticsAlertEvent evt, CancellationToken ct = default)
        {
            AnalyticsAlerts.Add((tenantId, evt));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public List<NotificationRequest> Requests { get; } = [];

        public Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
