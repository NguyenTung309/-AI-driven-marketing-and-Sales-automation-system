namespace Clawbot.Api.Contracts.Content;

public sealed record ContentBriefDto(
    Guid Id,
    string Platform,
    string Brief,
    string Status,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateContentBriefRequest(string Platform, string Brief);

public sealed record UpdateContentBriefRequest(string Platform, string Brief);

public sealed record UpdateContentBriefStatusRequest(string Status);

public sealed record ContentItemDto(
    Guid Id,
    Guid? BriefId,
    string Platform,
    string Status,
    string Body,
    string AssetsJson,
    Guid? CreatedBy,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GenerateContentItemRequest(
    Guid? BriefId,
    string? Platform,
    string? BriefText);

public sealed record GenerateContentItemResponse(IReadOnlyList<ContentItemDto> Items);

public sealed record GenerateImagePromptRequest(
    string? Brief,
    string? Platform,
    string? Style,
    IReadOnlyList<string>? BrandTokens);

public sealed record GenerateImagePromptResponse(
    string Prompt,
    string NegativePrompt,
    IReadOnlyDictionary<string, string> Hints);

public sealed record UpdateContentItemRequest(string Body, string? AssetsJson);

public sealed record ContentAssetUploadResponse(string Url, string AssetsJson);

public sealed record RejectContentItemRequest(string? Reason);

public sealed record RepurposeContentItemRequest(IReadOnlyList<string> TargetPlatforms);

public sealed record ContentQueueResponse(
    IReadOnlyList<ContentItemDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ScheduleContentItemRequest(DateTimeOffset? ScheduledAt);

public sealed record ContentScheduleDto(
    Guid Id,
    Guid ContentItemId,
    string Platform,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? PostedAt,
    string Status,
    string? PostUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ContentCalendarItemDto(
    Guid ScheduleId,
    Guid ContentItemId,
    string Platform,
    string Status,
    string Body,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? PostedAt,
    string? PostUrl);

public sealed record ContentCalendarResponse(IReadOnlyList<ContentCalendarItemDto> Items);

public sealed record TrendDto(
    string Topic,
    string Source,
    string Metric,
    double RelevanceScore,
    IReadOnlyList<string> ContentIdeas);

public sealed record TrendScanResponse(IReadOnlyList<TrendDto> Trends);
