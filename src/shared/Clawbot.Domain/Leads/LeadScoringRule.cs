using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads;

public sealed class LeadScoringRule : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string? Platform { get; private set; }
    public string EventCode { get; private set; } = string.Empty;
    public int Weight { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private LeadScoringRule() { }

    public static LeadScoringRule Create(Guid tenantId, string eventCode, int weight, string? platform, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventCode = eventCode,
            Weight = weight,
            Platform = platform,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void Deactivate() => IsActive = false;
}
