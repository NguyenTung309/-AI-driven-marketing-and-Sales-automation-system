using Clawbot.SharedKernel.Inbox;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

public sealed class SignalRInboxNotifier(IHubContext<InboxHub> hub) : IInboxNotifier
{
    private readonly IHubContext<InboxHub> _hub = hub;

    public Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Group(InboxHub.TenantGroup(tenantId)).SendAsync("message", evt, ct);

    public Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Group(InboxHub.TenantGroup(tenantId)).SendAsync("conversation", evt, ct);
}
