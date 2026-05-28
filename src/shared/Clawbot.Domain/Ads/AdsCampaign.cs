using Clawbot.Domain.Common;

namespace Clawbot.Domain.Ads;

public sealed class AdsCampaign : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Platform { get; private set; } = string.Empty;             // meta|tiktok
    public string ExternalCampaignId { get; private set; } = string.Empty;
    public string? Objective { get; private set; }
    public decimal? DailyBudget { get; private set; }
    public string? Status { get; private set; }
    public DateTimeOffset? SyncedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AdsCampaign() { }

    public static AdsCampaign Create(Guid tenantId, string platform, string externalCampaignId, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Platform = platform,
            ExternalCampaignId = externalCampaignId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
}
