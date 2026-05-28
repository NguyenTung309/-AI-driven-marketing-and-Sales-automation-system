using Clawbot.Domain.Common;

namespace Clawbot.Domain.Ads;

public sealed class AdsRule : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Metric { get; private set; } = string.Empty;       // cpl|frequency|ctr|spend
    public string Comparator { get; private set; } = string.Empty;   // gt|lt|gte|lte|eq
    public decimal Threshold { get; private set; }
    public string Action { get; private set; } = string.Empty;       // pause|scale_up|scale_down|alert
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AdsRule() { }

    public static AdsRule Create(
        Guid tenantId,
        string platform,
        string metric,
        string comparator,
        decimal threshold,
        string action,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Platform = platform,
            Metric = metric,
            Comparator = comparator,
            Threshold = threshold,
            Action = action,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void Deactivate() => IsActive = false;
}
