using Clawbot.Domain.Common;

namespace Clawbot.Domain.Experiments;

public sealed class ExperimentEvent : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ExperimentId { get; private set; }
    public Guid VariantId { get; private set; }
    public string SubjectKey { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public decimal? Value { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private ExperimentEvent() { }

    public static ExperimentEvent Create(
        Guid tenantId,
        Guid experimentId,
        Guid variantId,
        string subjectKey,
        string eventType,
        decimal? value,
        DateTimeOffset occurredAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExperimentId = experimentId,
            VariantId = variantId,
            SubjectKey = subjectKey.Trim(),
            EventType = eventType.Trim().ToLowerInvariant(),
            Value = value,
            OccurredAt = occurredAt,
        };
}
