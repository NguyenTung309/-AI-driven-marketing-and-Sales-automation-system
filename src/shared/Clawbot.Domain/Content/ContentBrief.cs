using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentBrief : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Brief { get; private set; } = string.Empty;
    public string Status { get; private set; } = "pending";
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ContentBrief() { }

    public static ContentBrief Create(Guid tenantId, string platform, string brief, Guid? createdBy, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Platform = platform,
            Brief = brief,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
}
