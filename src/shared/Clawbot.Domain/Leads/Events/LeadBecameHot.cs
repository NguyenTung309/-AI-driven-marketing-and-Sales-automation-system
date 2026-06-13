using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads.Events;

// PII-safe: identifiers + score only, no customer-derived content.
// Raised when a lead's score crosses into the 'hot' stage (>= 70) from a lower stage.
public sealed record LeadBecameHot(
    Guid TenantId,
    Guid LeadId,
    Guid? OwnerUserId,
    int Score,
    DateTimeOffset OccurredOn) : IDomainEvent;
