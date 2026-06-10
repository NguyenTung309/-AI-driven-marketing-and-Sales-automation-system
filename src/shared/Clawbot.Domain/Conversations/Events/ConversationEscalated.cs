using Clawbot.Domain.Common;

namespace Clawbot.Domain.Conversations.Events;

public sealed record ConversationEscalated(
    Guid TenantId,
    Guid ConversationId,
    DateTimeOffset OccurredOn) : IDomainEvent;
