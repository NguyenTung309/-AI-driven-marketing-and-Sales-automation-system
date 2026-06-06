namespace Clawbot.Api.Contracts.SaleAssist;

public sealed record SaleAssistDraftRequest(Guid ConversationId);
public sealed record SaleAssistDraftResponse(string DraftText, string SuggestedAction, int LeadScoreHint, long LatencyMs);

public sealed record SaleAssistSummaryRequest(Guid ConversationId);
public sealed record SaleAssistSummaryResponse(string Summary, long LatencyMs);

public sealed record QuickReplyDto(Guid Id, string Code, string? Category, string Body, string? Platforms);
public sealed record CreateQuickReplyRequest(string Code, string Body, string? Category, string? Platforms);
public sealed record UpdateQuickReplyRequest(string Body, string? Category, string? Platforms);
