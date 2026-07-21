using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads.Events;

// PII-safe: identifiers + score only, no customer-derived content.
public sealed record LeadReactivated(
    Guid TenantId,
    Guid LeadId,
    Guid? OwnerUserId,
    int Score,
    DateTimeOffset OccurredOn) : IDomainEvent;
