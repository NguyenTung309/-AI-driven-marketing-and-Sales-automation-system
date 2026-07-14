using Clawbot.SharedKernel.Jobs;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

/// <summary>Đẩy trạng thái job xuống group user (fallback group tenant khi job hệ thống).</summary>
public sealed class SignalRJobRealtime(IHubContext<NotificationHub> hub) : IJobRealtime
{
    public async Task JobUpdatedAsync(Guid tenantId, Guid? userId, object payload, CancellationToken ct = default)
    {
        var target = userId is { } uid
            ? hub.Clients.Group(NotificationHub.UserGroup(uid))
            : hub.Clients.Group(NotificationHub.TenantGroup(tenantId));
        await target.SendAsync("job", payload, ct).ConfigureAwait(false);
    }
}
