using System.Text.Json.Serialization;

namespace Clawbot.Api.Contracts.SaleAssist;

public sealed record SaleAssistDraftRequest(Guid ConversationId);
public sealed record SaleAssistDraftResponse(string DraftText, string SuggestedAction, int LeadScoreHint, long LatencyMs);
public sealed record SaleAssistDraftFeedbackRequest(Guid ConversationId, string DraftText, string? FinalText, string Outcome);
public sealed record SaleAssistDraftFeedbackResponse(Guid SessionId, bool Edited, DateTimeOffset RecordedAt);

public sealed record SaleAssistSummaryRequest(Guid ConversationId);
public sealed record SaleAssistSummaryResponse(string Summary, long LatencyMs);

public sealed record SaleAssistUpsellResponse(bool Eligible, string Suggestion, string Reason, int LeadScore);

public sealed record SaleAssistUpsellSuggestionsResponse(
    [property: JsonPropertyName("hot_leads")] IReadOnlyList<SaleAssistHotLeadDto> HotLeads,
    int Count);

public sealed record SaleAssistHotLeadDto(
    Guid Id,
    Guid? ConversationId,
    int Score,
    DateTimeOffset? LastActivityAt,
    SaleAssistHotLeadContactDto? Contact,
    bool Eligible,
    string Suggestion,
    string Reason);

public sealed record SaleAssistHotLeadContactDto(string? Name, string? Phone);

public sealed record QuickReplyDto(Guid Id, string Code, string? Category, string Body, string? Platforms);
public sealed record CreateQuickReplyRequest(string Code, string Body, string? Category, string? Platforms);
public sealed record UpdateQuickReplyRequest(string Body, string? Category, string? Platforms);
