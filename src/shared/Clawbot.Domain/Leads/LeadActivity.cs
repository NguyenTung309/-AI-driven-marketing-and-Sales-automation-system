using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads;

public sealed class LeadActivity : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid LeadId { get; private set; }
    public string ActivityType { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string MetaJson { get; private set; } = "{}";
    public DateTimeOffset OccurredAt { get; private set; }

    private LeadActivity() { }

    internal static LeadActivity Create(Guid tenantId, Guid leadId, string activityType, string notes, DateTimeOffset occurredAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeadId = leadId,
            ActivityType = activityType,
            Notes = notes,
            OccurredAt = occurredAt,
        };
}
