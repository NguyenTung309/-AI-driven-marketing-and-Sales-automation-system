using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads.Events;

// PII-safe: identifiers + score only, no customer-derived content.
// Raised when a lead's score crosses into the 'warm' stage (30-69) from a lower stage (cold).
public sealed record LeadBecameWarm(
    Guid TenantId,
    Guid LeadId,
    int Score,
    DateTimeOffset OccurredOn) : IDomainEvent;
