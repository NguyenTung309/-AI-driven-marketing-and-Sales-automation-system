using Clawbot.Domain.Common;

namespace Clawbot.Domain.Analytics;

public sealed class KpiDaily : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public int Leads { get; private set; }
    public int Dms { get; private set; }
    public int Replies { get; private set; }
    public int Conversions { get; private set; }
    public decimal? AvgResponseTimeSec { get; private set; }
    public decimal? AdSpend { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private KpiDaily() { }

    public static KpiDaily Create(
        Guid tenantId,
        DateOnly date,
        string platform,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Date = date,
            Platform = platform,
            CreatedAt = createdAt,
        };

    public void Record(int leads, int dms, int replies, int conversions, decimal? avgRespSec, decimal? adSpend)
    {
        Leads = leads;
        Dms = dms;
        Replies = replies;
        Conversions = conversions;
        AvgResponseTimeSec = avgRespSec;
        AdSpend = adSpend;
    }
}
