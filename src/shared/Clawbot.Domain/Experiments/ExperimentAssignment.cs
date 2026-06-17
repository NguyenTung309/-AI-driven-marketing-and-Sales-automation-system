using Clawbot.Domain.Common;

namespace Clawbot.Domain.Experiments;

public sealed class ExperimentAssignment : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ExperimentId { get; private set; }
    public Guid VariantId { get; private set; }
    public string SubjectKey { get; private set; } = string.Empty;
    public DateTimeOffset AssignedAt { get; private set; }

    private ExperimentAssignment() { }

    public static ExperimentAssignment Create(
        Guid tenantId,
        Guid experimentId,
        Guid variantId,
        string subjectKey,
        DateTimeOffset assignedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExperimentId = experimentId,
            VariantId = variantId,
            SubjectKey = subjectKey.Trim(),
            AssignedAt = assignedAt,
        };
}
