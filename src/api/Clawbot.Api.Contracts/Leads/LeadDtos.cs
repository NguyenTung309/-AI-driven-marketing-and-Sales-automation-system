namespace Clawbot.Api.Contracts.Leads;

public sealed record LeadDto(
    Guid Id,
    Guid? ContactId,
    Guid? OwnerUserId,
    int Score,
    string Stage,
    string? SourcePlatform,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt);

public sealed record CreateLeadRequest(
    Guid ContactId,
    string SourcePlatform,
    string? Phone,
    string? Email);

public sealed record CreateLeadResponse(Guid LeadId, IReadOnlyList<LeadDedupHitDto> Duplicates);

public sealed record LeadDedupHitDto(Guid LeadId, Guid ContactId, string Reason, float Confidence);

public sealed record LeadActivityRequest(string EventCode, string? Platform, string? Notes);
public sealed record LeadActivityResponse(int NewScore, string Stage, string Reason, IReadOnlyList<string> MatchedRules);

public sealed record LeadAssignRequest(Guid? UserId);

public sealed record LeadScoringRuleDto(Guid Id, string EventCode, string? Platform, int Weight, bool IsActive, string? Description);
public sealed record CreateLeadScoringRuleRequest(string EventCode, int Weight, string? Platform, string? Description);

public sealed record CreateWithSkillsResult(
    Guid LeadId,
    bool SpamFlagged,
    string SpamReason,
    string Timezone,
    string EnrichmentCompany,
    bool PossibleDup,
    IReadOnlyList<LeadDedupCandidateDto> DedupCandidates);

public sealed record LeadDedupCandidateDto(Guid ContactId, float Similarity);
