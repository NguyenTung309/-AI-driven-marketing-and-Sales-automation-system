namespace Clawbot.Api.Contracts.Competitors;

public sealed record CompetitorSourceDto(
    Guid Id,
    string Name,
    string Url,
    string SourceType,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastScannedAt);

public sealed record CreateCompetitorSourceRequest(string Name, string Url, string? SourceType);

public sealed record UpdateCompetitorSourceRequest(string Name, string Url, string? SourceType, bool IsActive);

public sealed record CompetitorPostDto(
    Guid Id,
    Guid SourceId,
    string Url,
    string Title,
    string? Snippet,
    DateTimeOffset PublishedAt,
    DateTimeOffset DetectedAt);
