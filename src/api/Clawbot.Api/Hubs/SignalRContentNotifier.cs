using Clawbot.SharedKernel.Content;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

public sealed class SignalRContentNotifier(IHubContext<DashboardHub> hub) : IContentNotifier
{
    private readonly IHubContext<DashboardHub> _hub = hub;

    public Task NotifyTrendScanAsync(Guid tenantId, ContentTrendScanEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId)).SendAsync("content.trends.scanned", evt, ct);

    public Task NotifyPublishFailedAsync(Guid tenantId, ContentPublishFailedEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId)).SendAsync("content.publish.failed", evt, ct);

    public Task NotifyAnalyticsAlertAsync(Guid tenantId, AnalyticsAlertEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId)).SendAsync("analytics.alert", evt, ct);
}
