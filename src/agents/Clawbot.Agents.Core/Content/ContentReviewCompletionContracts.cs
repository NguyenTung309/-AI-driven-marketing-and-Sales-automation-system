namespace Clawbot.Agents.Core.Content;

// Phase 2.5: strict provider-neutral review completion contract. General chat keeps
// IClaudeChatClient; automatic review gates use this envelope + closed-schema parser.

public enum ReviewPromptRole
{
    TrustedSystem = 0,
    UntrustedUser = 1,
}

public enum ReviewPromptPartKind
{
    Text = 0,
    ImageBytes = 1,
}

public sealed class ReviewPromptPart
{
    private ReviewPromptPart(
        ReviewPromptRole role,
        ReviewPromptPartKind kind,
        string? text,
        string? partId,
        string? mediaType,
        byte[]? bytes)
    {
        Role = role;
        Kind = kind;
        Text = text;
        PartId = partId;
        MediaType = mediaType;
        Bytes = bytes;
    }

    public ReviewPromptRole Role { get; }
    public ReviewPromptPartKind Kind { get; }
    public string? Text { get; }
    public string? PartId { get; }
    public string? MediaType { get; }
    public IReadOnlyList<byte>? Bytes { get; }

    public static ReviewPromptPart TrustedSystem(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new(
            ReviewPromptRole.TrustedSystem,
            ReviewPromptPartKind.Text,
            text.Trim(),
            partId: null,
            mediaType: null,
            bytes: null);
    }

    public static ReviewPromptPart UntrustedText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new(
            ReviewPromptRole.UntrustedUser,
            ReviewPromptPartKind.Text,
            text,
            partId: null,
            mediaType: null,
            bytes: null);
    }

    public static ReviewPromptPart UntrustedImageBytes(
        string partId,
        string mediaType,
        byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new ArgumentException("image_bytes_required", nameof(bytes));

        return new(
            ReviewPromptRole.UntrustedUser,
            ReviewPromptPartKind.ImageBytes,
            text: null,
            partId.Trim(),
            mediaType.Trim().ToLowerInvariant(),
            bytes);
    }
}

public static class ReviewCompletionFinishReasons
{
    public const string EndTurn = "end_turn";
    public const string Stop = "stop";
}

public sealed record ReviewCompletionEnvelope(
    string RawText,
    bool ObservedTerminalSuccess,
    string FinishReason,
    bool IsRefused,
    bool IsContentFiltered,
    bool IsTruncated,
    IReadOnlyList<string> RequestedPartIds,
    IReadOnlyList<string> SentPartIds,
    int InputTokens = 0,
    int OutputTokens = 0,
    decimal UsdCost = 0m,
    string Model = "")
{
    public string RawText { get; } = RawText ?? string.Empty;
    public string FinishReason { get; } = FinishReason ?? string.Empty;
    public IReadOnlyList<string> RequestedPartIds { get; } = RequestedPartIds ?? [];
    public IReadOnlyList<string> SentPartIds { get; } = SentPartIds ?? [];
    public string Model { get; } = Model ?? string.Empty;
}

public sealed record StrictContentReviewOutcome(
    bool IsAccepted,
    string ReviewStatus,
    string ReasonCode,
    string? Reason,
    string? ErrorCode,
    IReadOnlyList<string>? ReviewedPartIds = null)
{
    public IReadOnlyList<string> ReviewedPartIds { get; } = ReviewedPartIds ?? [];
}

public interface IContentReviewCompletionClient
{
    Task<ReviewCompletionEnvelope> CompleteTextAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedTextParts,
        CancellationToken cancellationToken);

    Task<ReviewCompletionEnvelope> CompleteVisionAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedContentParts,
        CancellationToken cancellationToken);
}

// Phase 2.7/2.8: builds a review-specific provider adapter. Does not reuse ClaudeReply chat clients.
public interface IContentReviewCompletionClientFactory
{
    IContentReviewCompletionClient Create(Chat.ResolvedLlmConfig config);
}

// Machine-readable provider/model rejection of image content parts only.
// Auth/transport/timeout/schema errors must NOT use this type.
public sealed class VisionUnsupportedException : Exception
{
    public VisionUnsupportedException(string message)
        : base(message)
    {
    }

    public VisionUnsupportedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ContentItemReviewLimits
{
    public const int MaxReasonLength = 1024;
}
