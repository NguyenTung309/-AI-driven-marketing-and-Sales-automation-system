using Clawbot.SharedKernel.Inbox;

namespace Clawbot.Infrastructure.Notifications;

public sealed class NoopInboxNotifier : IInboxNotifier
{
    public Task NotifyMessageAsync(Guid tenantId, InboxMessageEvent evt, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyMessageStatusAsync(Guid tenantId, InboxMessageStatusEvent evt, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyConversationUpdatedAsync(Guid tenantId, InboxConversationEvent evt, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NotifyTypingAsync(Guid tenantId, InboxTypingEvent evt, CancellationToken ct = default) =>
        Task.CompletedTask;
}
