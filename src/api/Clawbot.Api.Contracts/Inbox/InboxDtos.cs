namespace Clawbot.Api.Contracts.Inbox;

public sealed record ConversationListItemDto(
    Guid Id,
    string Platform,
    string ExternalThreadId,
    string Status,
    Guid? ContactId,
    string? ContactDisplayName,
    Guid? AssignedTo,
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview,
    int UnreadCount);

public sealed record ConversationDetailDto(
    Guid Id,
    string Platform,
    string ExternalThreadId,
    string Status,
    Guid? ContactId,
    string? ContactDisplayName,
    Guid? AssignedTo,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MessageDto> Messages);

public sealed record MessageDto(
    Guid Id,
    string Direction,
    string SenderType,
    Guid? SenderUserId,
    string Content,
    string ContentType,
    DateTimeOffset SentAt);

public sealed record AssignConversationRequest(Guid UserId);

public sealed record SendMessageRequest(string Content, string ContentType = "text");

public sealed record ConversationListResponse(
    IReadOnlyList<ConversationListItemDto> Items,
    int Total,
    int Page,
    int PageSize);
