using Clawbot.Domain.Common;

namespace Clawbot.Domain.Competitors;

// Research-2: a post/campaign detected on a competitor source. ContentHash dedupes re-scans.
// Note: distinct from the in-memory Clawbot.Agents.Core.Skills.Content.CompetitorPost scan DTO.
public sealed class CompetitorPost : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid SourceId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Snippet { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;

    private CompetitorPost() { }

    public static CompetitorPost Create(
        Guid tenantId,
        Guid sourceId,
        string url,
        string title,
        string? snippet,
        DateTimeOffset publishedAt,
        DateTimeOffset detectedAt,
        string contentHash) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceId = sourceId,
            Url = url,
            Title = title,
            Snippet = snippet,
            PublishedAt = publishedAt,
            DetectedAt = detectedAt,
            ContentHash = contentHash,
        };
}
