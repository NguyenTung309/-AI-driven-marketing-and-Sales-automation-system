using Clawbot.Domain.Common;

namespace Clawbot.Domain.Competitors;

// Research-2: an admin-configured competitor feed (RSS or fanpage URL) scanned per tenant.
public sealed class CompetitorSource : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Url { get; private set; } = string.Empty;
    public string SourceType { get; private set; } = "rss"; // rss|fanpage
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastScannedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private CompetitorSource() { }

    public static CompetitorSource Create(Guid tenantId, string name, string url, string sourceType, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Url = url,
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? "rss" : sourceType,
            IsActive = true,
            CreatedAt = now,
        };

    public void Update(string name, string url, string sourceType, bool isActive)
    {
        Name = name;
        Url = url;
        SourceType = string.IsNullOrWhiteSpace(sourceType) ? SourceType : sourceType;
        IsActive = isActive;
    }

    public void MarkScanned(DateTimeOffset at) => LastScannedAt = at;

    public void SoftDelete(DateTimeOffset at)
    {
        IsActive = false;
        DeletedAt = at;
    }
}
