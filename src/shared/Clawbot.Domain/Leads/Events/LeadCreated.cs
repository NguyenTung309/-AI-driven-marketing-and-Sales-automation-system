using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads.Events;

// PII-safe: identifiers + platform only, no customer-derived content.
public sealed record LeadCreated(
    Guid TenantId,
    Guid LeadId,
    Guid? ContactId,
    string? SourcePlatform,
    DateTimeOffset OccurredOn) : IDomainEvent;
