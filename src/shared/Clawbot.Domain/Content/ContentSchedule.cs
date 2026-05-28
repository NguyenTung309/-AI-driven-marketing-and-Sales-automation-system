using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentSchedule : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ContentItemId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public string Status { get; private set; } = "pending";
    public string? PostUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ContentSchedule() { }

    public static ContentSchedule Schedule(
        Guid tenantId,
        Guid contentItemId,
        string platform,
        DateTimeOffset scheduledAt,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContentItemId = contentItemId,
            Platform = platform,
            ScheduledAt = scheduledAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void MarkPosted(string postUrl, DateTimeOffset at)
    {
        Status = "posted";
        PostedAt = at;
        PostUrl = postUrl;
    }

    public void MarkFailed() => Status = "failed";
}
