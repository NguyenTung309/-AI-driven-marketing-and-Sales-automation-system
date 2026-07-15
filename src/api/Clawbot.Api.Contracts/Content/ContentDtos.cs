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

public sealed record ContentQueueCursorPage(
    IReadOnlyList<ContentItemDto> Items,
    string? NextCursor,
    int? Total);

public sealed record ContentQueueResponse(
    IReadOnlyList<ContentItemDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ScheduleContentItemRequest(DateTimeOffset? ScheduledAt, Guid? MetaAssetId = null);

public sealed record ContentPublishTargetDto(
    Guid Id,
    string Platform,
    string ExternalId,
    string Name,
    bool IsDefault);

public sealed record ContentScheduleDto(
    Guid Id,
    Guid ContentItemId,
    string Platform,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? PostedAt,
    string Status,
    string? PostUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? MetaAssetId,
    int? LikeCount = null,
    int? CommentCount = null,
    DateTimeOffset? EngagementSyncedAt = null);

public sealed record ContentCalendarItemDto(
    Guid ScheduleId,
    Guid ContentItemId,
    string Platform,
    string Status,
    string Body,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? PostedAt,
    string? PostUrl,
    Guid? MetaAssetId,
    int? LikeCount = null,
    int? CommentCount = null);

public sealed record ContentCalendarResponse(IReadOnlyList<ContentCalendarItemDto> Items);

public sealed record TrendDto(
    string Topic,
    string Source,
    string Metric,
    double RelevanceScore,
    IReadOnlyList<string> ContentIdeas,
    string WeekOf = "");

public sealed record TrendScanResponse(IReadOnlyList<TrendDto> Trends);

public sealed record TrendSourceSettingDto(bool Enabled, bool HasApiKey, string? Url);

public sealed record TrendScheduleDto(string Cadence, DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt);

public sealed record TrendSettingsResponse(
    string Geo,
    TrendSourceSettingDto Google,
    TrendSourceSettingDto YouTube,
    TrendSourceSettingDto TikTok,
    TrendScheduleDto Schedule);

// ApiKey/Url semantics: null = keep current value, empty string = clear it.
public sealed record UpdateTrendSourceSetting(bool? Enabled = null, string? ApiKey = null, string? Url = null);

public sealed record UpdateTrendSettingsRequest(
    string? Geo = null,
    UpdateTrendSourceSetting? Google = null,
    UpdateTrendSourceSetting? YouTube = null,
    UpdateTrendSourceSetting? TikTok = null,
    string? ScheduleCadence = null);
