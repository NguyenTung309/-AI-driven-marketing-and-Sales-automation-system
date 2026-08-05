using Clawbot.SharedKernel.Inbox;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

public sealed class SignalRInboxNotifier(IHubContext<InboxHub> hub) : IInboxNotifier
{
    private readonly IHubContext<InboxHub> _hub = hub;

    public Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Groups(TargetGroups(tenantId, evt.InboxId, evt.AssignedTo))
            .SendAsync("message", evt, ct);

    public Task NotifyMessageStatusAsync(Guid tenantId, InboxMessageStatusEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Groups(TargetGroups(tenantId, evt.InboxId, evt.AssignedTo))
            .SendAsync("messageStatus", evt, ct);

    public Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Groups(TargetGroups(tenantId, evt.InboxId, evt.AssignedTo))
            .SendAsync("conversation", evt, ct);

    public Task NotifyTypingAsync(Guid tenantId, InboxTypingEvent evt, CancellationToken ct = default) =>
        _hub.Clients.Groups(TargetGroups(tenantId, evt.InboxId, evt.AssignedTo))
            .SendAsync("typing", evt, ct);

    private static List<string> TargetGroups(Guid tenantId, Guid? inboxId, Guid? assignedTo)
    {
        var groups = new HashSet<string>(StringComparer.Ordinal)
        {
            InboxHub.AdminGroup(tenantId),
        };
        if (inboxId.HasValue)
            groups.Add(InboxHub.InboxGroup(inboxId.Value));
        if (assignedTo.HasValue)
            groups.Add(InboxHub.UserGroup(assignedTo.Value));
        return groups.ToList();
    }
}
