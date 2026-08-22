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

public sealed record ContentAgentReviewDto(
    string Status,
    int? ReviewedRevision,
    Guid? ReviewedByAgentId,
    DateTimeOffset? ReviewedAt,
    string? Reason,
    string ImageReviewStatus,
    int ReviewedImageCount);

public sealed record ContentPublishingApprovalDto(
    string Status,
    string? PolicyApplied,
    long? PolicyVersionApplied,
    int? ApprovedRevision,
    string? Mode,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? Reason,
    string? RequirementReason);

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
    DateTimeOffset UpdatedAt,
    int ContentRevision = 1,
    ContentAgentReviewDto? AgentReview = null,
    ContentPublishingApprovalDto? PublishingApproval = null,
    string WorkflowState = "awaiting_agent_review",
    bool CanApprove = false,
    bool CanReject = false,
    bool CanRetryReview = false,
    bool CanSchedule = false,
    bool CanPublish = false,
    string? ScheduleBlockedReason = null);

public sealed record ContentPublishingPolicyDto(
    bool AgentReviewRequired,
    string AgentReviewMode,
    string ReviewerVisionCapability,
    string PublishingApprovalPolicy,
    long PolicyVersion,
    DateTimeOffset UpdatedAt);

public sealed record UpdateContentPublishingPolicyRequest(string PublishingApprovalPolicy);

public sealed record RetryAgentReviewRequest(int ExpectedRevision);

public sealed record ReconcilePublishRequest(
    string Outcome,
    string? ExternalPostId = null,
    string? ErrorCode = null);

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

// Url remains a derived display link; AssetId + StorageKey never accept client-supplied values.
public sealed record ContentAssetUploadResponse(string Url, string AssetsJson, Guid AssetId = default);

public sealed record ApproveContentItemRequest(int ExpectedRevision, string? OverrideReason = null);

public sealed record RejectContentItemRequest(int ExpectedRevision, string? Reason);

public sealed record RepurposeContentItemRequest(IReadOnlyList<string>? TargetPlatforms);

// P5 §4.5: đổi hook. GET trả danh sách hook L2 đã lưu + hook đang chọn; POST chọn hookIndex để chạy lại L3+L4.
public sealed record ContentHookOptionDto(int Index, string Text, bool Selected);

public sealed record ContentItemHooksResponse(
    bool Available,
    IReadOnlyList<ContentHookOptionDto> Hooks);

public sealed record RegenerateHookApiRequest(int HookIndex);

// P5 §6: dashboard vận hành chuỗi sinh nội dung, tổng hợp từ content_generation_traces + content_items.
public sealed record ContentChainStepMetricDto(
    string StepId,
    int Attempts,
    int GateFailures,
    double GateFailRate,
    long P95LatencyMs);

public sealed record ContentChainMetricsResponse(
    int WindowDays,
    int TotalRuns,
    int FallbackRuns,
    double FallbackRate,
    double AvgTokensPerRun,
    double AvgUsdCostPerRun,
    IReadOnlyList<ContentChainStepMetricDto> Steps,
    int ReviewApproved,
    int ReviewTotal,
    double ReviewApproveRate);

public sealed record ContentPostPerformanceTotalsDto(
    int Posts,
    int SyncedPosts,
    long? Likes,
    long? Comments,
    double? AvgEngagementPerPost);

public sealed record ContentPostPerformanceFreshnessDto(
    int SyncedPosts,
    int UnsyncedPosts,
    DateTimeOffset? OldestEngagementAttemptAt);

public sealed record ContentPostPerformancePlatformDto(
    string Platform,
    int Posts,
    int SyncedPosts,
    long? Likes,
    long? Comments,
    double? AvgEngagementPerPost);

public sealed record ContentPostPerformanceTargetDto(
    Guid? MetaAssetId,
    string TargetName,
    int Posts,
    int SyncedPosts,
    long? Likes,
    long? Comments,
    double? AvgEngagementPerPost);

public sealed record ContentPostPerformanceDailyDto(
    DateOnly Date,
    int Posts,
    int SyncedPosts,
    long? Likes,
    long? Comments);

public sealed record ContentPostPerformanceTopPostDto(
    Guid ScheduleId,
    Guid ContentItemId,
    bool IsContentAvailable,
    string Platform,
    string Excerpt,
    string? PostUrl,
    DateTimeOffset PostedAt,
    int? Likes,
    int? Comments,
    long? Total,
    // Dialog xem bài trong app cần biết bài đã lên trang nào và số tương tác cũ tới mức nào.
    Guid? MetaAssetId = null,
    string? TargetName = null,
    DateTimeOffset? EngagementSyncedAt = null,
    // Tong moi loai reaction (Likes chi la loai LIKE). null = chua dong bo sau khi co tinh nang nay.
    int? ReactionsTotal = null,
    int? ReactionLove = null,
    int? ReactionHaha = null,
    int? ReactionWow = null,
    int? ReactionSad = null,
    int? ReactionAngry = null,
    int? ReactionCare = null);

public sealed record ContentPostCommentDto(
    string Id,
    string AuthorName,
    string Message,
    DateTimeOffset? CreatedAt,
    int LikeCount,
    int ReplyCount);

public sealed record ContentPostCommentsResponse(
    Guid ScheduleId,
    IReadOnlyList<ContentPostCommentDto> Items,
    int TotalCount,
    bool IsTruncated,
    string? UnavailableReason);

public sealed record ContentPostPerformanceResponse(
    int WindowDays,
    DateTimeOffset From,
    DateTimeOffset To,
    ContentPostPerformanceTotalsDto Totals,
    ContentPostPerformanceFreshnessDto Freshness,
    IReadOnlyList<ContentPostPerformancePlatformDto> ByPlatform,
    IReadOnlyList<ContentPostPerformanceTargetDto> ByTarget,
    IReadOnlyList<ContentPostPerformanceDailyDto> Daily,
    IReadOnlyList<ContentPostPerformanceTopPostDto> TopPosts);

public sealed record ContentQueueCursorPage(
    IReadOnlyList<ContentItemDto> Items,
    string? NextCursor,
    int? Total);

public sealed record ContentQueueResponse(
    IReadOnlyList<ContentItemDto> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ScheduleContentItemRequest(
    DateTimeOffset? ScheduledAt,
    Guid? MetaAssetId = null,
    bool ConfirmInstagramAccount = false);

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
    DateTimeOffset? EngagementSyncedAt = null,
    int RetryCount = 0,
    string? LastError = null);

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
    int? CommentCount = null,
    int RetryCount = 0,
    string? LastError = null,
    bool RequiresInstagramAccountConfirmation = false);

public sealed record ContentCalendarResponse(IReadOnlyList<ContentCalendarItemDto> Items);

public sealed record TrendDto(
    string Topic,
    string Source,
    string Metric,
    double RelevanceScore,
    IReadOnlyList<string> ContentIdeas,
    string WeekOf = "");

public sealed record RawTrendDto(string Topic, string Source, string Metric, double SourceScore, IReadOnlyList<string> ContentIdeas);

public sealed record TrendScanResponse(IReadOnlyList<TrendDto> Trends, IReadOnlyList<RawTrendDto>? RawTrends = null);

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
