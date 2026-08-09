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
    // Sub-set cua Dms: hoi thoai trong ngay co it nhat 1 phan hoi outbound cua AI.
    // Khac Replies (dem theo tin nhan) o cho 1 hoi thoai chi tinh 1 lan du co nhieu luot qua lai trong ngay,
    // nen luon <= Dms - dung de tinh ti le tu dong hoa dung ban chat, khong vuot 100%.
    public int RepliedDms { get; private set; }
    public int Conversions { get; private set; }
    public decimal? AvgResponseTimeSec { get; private set; }
    public decimal? AdSpend { get; private set; }
    public decimal? Revenue { get; private set; }
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

    public void Record(
        int leads,
        int dms,
        int replies,
        int repliedDms,
        int conversions,
        decimal? avgRespSec,
        decimal? adSpend,
        decimal? revenue = null)
    {
        Leads = leads;
        Dms = dms;
        Replies = replies;
        RepliedDms = repliedDms;
        Conversions = conversions;
        AvgResponseTimeSec = avgRespSec;
        AdSpend = adSpend;
        Revenue = revenue;
    }
}
