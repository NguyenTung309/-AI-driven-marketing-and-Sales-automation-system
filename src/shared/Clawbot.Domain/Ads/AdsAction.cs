using Clawbot.Domain.Common;

namespace Clawbot.Domain.Ads;

public sealed class AdsAction : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid? RuleId { get; private set; }
    public string ActionTaken { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public DateTimeOffset ExecutedAt { get; private set; }

    private AdsAction() { }

    public static AdsAction Create(
        Guid tenantId,
        Guid campaignId,
        Guid? ruleId,
        string actionTaken,
        string payloadJson,
        DateTimeOffset executedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CampaignId = campaignId,
            RuleId = ruleId,
            ActionTaken = actionTaken,
            PayloadJson = payloadJson,
            ExecutedAt = executedAt,
        };
}
