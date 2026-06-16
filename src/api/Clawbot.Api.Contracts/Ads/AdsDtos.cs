namespace Clawbot.Api.Contracts.Ads;

public sealed record AdsRuleDto(
    Guid Id,
    string Platform,
    string Metric,
    string Comparator,
    decimal Threshold,
    string Action,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateAdsRuleRequest(
    string Platform,
    string Metric,
    string Comparator,
    decimal Threshold,
    string Action);

public sealed record UpdateAdsRuleRequest(
    string Platform,
    string Metric,
    string Comparator,
    decimal Threshold,
    string Action);

public sealed record AdsCampaignDto(
    Guid Id,
    string Platform,
    string ExternalCampaignId,
    string? Objective,
    decimal? DailyBudget,
    string? Status,
    decimal? TargetCpl,
    bool DaypartPaused,
    DateTimeOffset? SyncedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateTargetCplRequest(decimal? TargetCpl);

public sealed record AdsActionDto(
    Guid Id,
    Guid CampaignId,
    Guid? RuleId,
    string ActionTaken,
    string PayloadJson,
    DateTimeOffset ExecutedAt);

public sealed record AdsEvaluateRequestDto(
    string Platform,
    Guid CampaignId);

public sealed record AdsEvaluateResponseDto(
    IReadOnlyList<AdsActionExecutedDto> Actions);

public sealed record AdsActionExecutedDto(
    Guid? RuleId,
    string Action,
    string Note);

public sealed record AdsLookalikeRequestDto(
    string Platform,
    IReadOnlyList<string> SeedContactKeys);

public sealed record AdsLookalikeResponseDto(
    string? AudienceId,
    bool Created);
