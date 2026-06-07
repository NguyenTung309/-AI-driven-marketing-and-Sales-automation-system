using Clawbot.Domain.Common;

namespace Clawbot.Domain.Ads;

public sealed class AdsCreative : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid CampaignId { get; private set; }
    public string ExternalCreativeId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "active";  // active|standby
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AdsCreative() { }

    public static AdsCreative Create(
        Guid tenantId,
        Guid campaignId,
        string externalCreativeId,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CampaignId = campaignId,
            ExternalCreativeId = externalCreativeId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void Activate(DateTimeOffset at)
    {
        Status = "active";
        UpdatedAt = at;
    }

    public void Standby(DateTimeOffset at)
    {
        Status = "standby";
        UpdatedAt = at;
    }
}
