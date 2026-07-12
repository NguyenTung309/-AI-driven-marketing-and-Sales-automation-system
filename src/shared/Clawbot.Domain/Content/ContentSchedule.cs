using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentSchedule : AggregateRoot<Guid>, ITenantOwned
{
    public const int MaxRetries = 3;

    public Guid TenantId { get; private set; }
    public Guid ContentItemId { get; private set; }
    public Guid? MetaAssetId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public string Status { get; private set; } = "pending";
    public string? PostUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int RetryCount { get; private set; }
    // Engagement counts fetched back from the platform (FB Graph) after publishing. Null = not synced yet.
    public int? LikeCount { get; private set; }
    public int? CommentCount { get; private set; }
    public DateTimeOffset? EngagementSyncedAt { get; private set; }

    private ContentSchedule() { }

    public static ContentSchedule Schedule(
        Guid tenantId,
        Guid contentItemId,
        string platform,
        DateTimeOffset scheduledAt,
        DateTimeOffset createdAt,
        Guid? metaAssetId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContentItemId = contentItemId,
            MetaAssetId = metaAssetId,
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
        UpdatedAt = at;
    }

    public void MarkFailed(DateTimeOffset at)
    {
        Status = "failed";
        UpdatedAt = at;
    }

    public bool RecordRetry(DateTimeOffset at)
    {
        RetryCount++;
        UpdatedAt = at;
        if (RetryCount >= MaxRetries)
        {
            Status = "failed";
            return false;
        }

        return true;
    }

    public void SetEngagement(int? likeCount, int? commentCount, DateTimeOffset at)
    {
        LikeCount = likeCount;
        CommentCount = commentCount;
        EngagementSyncedAt = at;
        UpdatedAt = at;
    }

    public void Cancel(DateTimeOffset at)
    {
        Status = "canceled";
        UpdatedAt = at;
    }
}
