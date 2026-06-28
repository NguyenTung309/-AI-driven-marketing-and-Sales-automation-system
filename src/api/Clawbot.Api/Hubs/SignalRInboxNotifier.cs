using Clawbot.SharedKernel.Inbox;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

public sealed class SignalRInboxNotifier(IHubContext<InboxHub> hub) : IInboxNotifier
{
    private readonly IHubContext<InboxHub> _hub = hub;

    public Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default)
    {
        var groups = new List<string> { InboxHub.TenantGroup(tenantId) };
        if (evt.AssignedTo.HasValue)
            groups.Add(InboxHub.UserGroup(evt.AssignedTo.Value));
        return _hub.Clients.Groups(groups).SendAsync("message", evt, ct);
    }

    public Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default)
    {
        var groups = new List<string> { InboxHub.TenantGroup(tenantId) };
        if (evt.AssignedTo.HasValue)
            groups.Add(InboxHub.UserGroup(evt.AssignedTo.Value));
        return _hub.Clients.Groups(groups).SendAsync("conversation", evt, ct);
    }
}