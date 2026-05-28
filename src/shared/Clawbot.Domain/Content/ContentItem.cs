using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentItem : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid? BriefId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Status { get; private set; } = "draft";  // draft|approved|scheduled|published|rejected
    public string Body { get; private set; } = string.Empty;
    public string AssetsJson { get; private set; } = "[]";
    public Guid? CreatedBy { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private ContentItem() { }

    public static ContentItem Create(Guid tenantId, string platform, string body, Guid? createdBy, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Platform = platform,
            Body = body,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void Approve(Guid approverUserId, DateTimeOffset at)
    {
        Status = "approved";
        ApprovedBy = approverUserId;
        ApprovedAt = at;
    }

    public void Reject() => Status = "rejected";
}
