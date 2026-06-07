using Clawbot.Domain.Common;

namespace Clawbot.Domain.Ads;

public sealed class AdsMetricsDaily : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid CampaignId { get; private set; }
    public DateOnly MetricDate { get; private set; }
    public decimal? Cpl { get; private set; }
    public decimal? Frequency { get; private set; }
    public decimal? Ctr { get; private set; }
    public decimal? Spend { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AdsMetricsDaily() { }

    public static AdsMetricsDaily Create(
        Guid tenantId,
        Guid campaignId,
        DateOnly metricDate,
        decimal? cpl,
        decimal? frequency,
        decimal? ctr,
        decimal? spend,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CampaignId = campaignId,
            MetricDate = metricDate,
            Cpl = cpl,
            Frequency = frequency,
            Ctr = ctr,
            Spend = spend,
            CreatedAt = createdAt,
        };
}
