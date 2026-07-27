using System.Text;
using System.Text.Json;

namespace Clawbot.Agents.Core.Content;

// Phase 2.5/2.12: evaluate terminal/refusal/filter/truncation envelope, then parse the entire
// trimmed output as exactly one closed-schema JSON object. No prose/fences/substring extraction.
// Vision path additionally requires reviewedPartIds matching requested/sent completeness.
public static class StrictContentReviewOutcomeParser
{
    private static readonly HashSet<string> AllowedFinishReasons = new(StringComparer.Ordinal)
    {
        ReviewCompletionFinishReasons.EndTurn,
        ReviewCompletionFinishReasons.Stop,
    };

    private static readonly HashSet<string> AllowedVerdicts = new(StringComparer.Ordinal)
    {
        ContentReviewResult.Approve,
        ContentReviewResult.RejectVerdict,
        ContentReviewResult.NeedsHuman,
    };

    public static StrictContentReviewOutcome Parse(ReviewCompletionEnvelope envelope)
        => Parse(envelope, requireReviewedPartIds: false);

    public static StrictContentReviewOutcome ParseVision(ReviewCompletionEnvelope envelope)
        => Parse(envelope, requireReviewedPartIds: true);

    public static StrictContentReviewOutcome Parse(
        ReviewCompletionEnvelope envelope,
        bool requireReviewedPartIds)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!envelope.ObservedTerminalSuccess)
            return Rejected("review_terminal_incomplete");
        if (envelope.IsRefused)
            return Rejected("review_refused");
        if (envelope.IsContentFiltered)
            return Rejected("review_content_filtered");
        if (envelope.IsTruncated)
            return Rejected("review_truncated");

        var finishReason = envelope.FinishReason.Trim();
        if (finishReason.Length == 0)
            return Rejected("review_finish_reason_missing");
        if (finishReason is "max_tokens" or "length")
            return Rejected("review_truncated");
        if (!AllowedFinishReasons.Contains(finishReason))
            return Rejected("review_finish_reason_disallowed");

        var raw = envelope.RawText.Trim();
        if (raw.Length == 0)
            return Rejected("review_empty_output");

        var outcome = ParseExactClosedSchema(raw, requireReviewedPartIds);
        if (!outcome.IsAccepted)
            return outcome;

        if (requireReviewedPartIds)
        {
            var completeness = ValidatePartIdCompleteness(
                envelope.RequestedPartIds,
                envelope.SentPartIds,
                outcome.ReviewedPartIds);
            if (completeness is not null)
                return Rejected(completeness);
        }

        return outcome;
    }

    // Legacy ContentReviewer entry: text-only path still fail-closed, but now requires exact JSON.
    public static ContentReviewResult ParseLegacyVerdict(string text)
    {
        var envelope = new ReviewCompletionEnvelope(
            RawText: text ?? string.Empty,
            ObservedTerminalSuccess: true,
            FinishReason: ReviewCompletionFinishReasons.EndTurn,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: [],
            SentPartIds: []);
        var outcome = Parse(envelope);
        if (!outcome.IsAccepted)
            return new ContentReviewResult(ContentReviewResult.NeedsHuman, outcome.ErrorCode ?? "review_parse_failed");

        return outcome.ReviewStatus switch
        {
            "passed" => new ContentReviewResult(ContentReviewResult.Approve, outcome.Reason ?? string.Empty),
            "rejected" => new ContentReviewResult(ContentReviewResult.RejectVerdict, outcome.Reason ?? string.Empty),
            "needs_human" => new ContentReviewResult(ContentReviewResult.NeedsHuman, outcome.Reason ?? string.Empty),
            _ => new ContentReviewResult(ContentReviewResult.NeedsHuman, "review_unknown_verdict"),
        };
    }

    public static string? ValidatePartIdCompleteness(
        IReadOnlyList<string> requested,
        IReadOnlyList<string> sent,
        IReadOnlyList<string> reviewed)
    {
        if (!SetsEqual(requested, sent) || requested.Count != sent.Count)
            return "review_part_ids_incomplete";
        if (!SetsEqual(requested, reviewed) || requested.Count != reviewed.Count)
            return "review_part_ids_incomplete";
        if (requested.Count == 0)
            return "review_part_ids_incomplete";
        return null;
    }

    private static bool SetsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        var left = new HashSet<string>(a, StringComparer.Ordinal);
        if (left.Count != a.Count)
            return false; // duplicates
        var right = new HashSet<string>(b, StringComparer.Ordinal);
        if (right.Count != b.Count)
            return false;
        return left.SetEquals(right);
    }

    private static StrictContentReviewOutcome ParseExactClosedSchema(
        string raw,
        bool requireReviewedPartIds)
    {
        try
        {
            var utf8 = Encoding.UTF8.GetBytes(raw);
            var reader = new Utf8JsonReader(
                utf8,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return Rejected("review_parse_failed");

            string? verdict = null;
            string? reason = null;
            List<string>? reviewedPartIds = null;
            var seenVerdict = false;
            var seenReason = false;
            var seenReviewedPartIds = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    return Rejected("review_parse_failed");

                var propertyName = reader.GetString();
                if (!reader.Read())
                    return Rejected("review_parse_failed");

                if (propertyName == "verdict")
                {
                    if (seenVerdict || reader.TokenType != JsonTokenType.String)
                        return Rejected("review_parse_failed");
                    seenVerdict = true;
                    verdict = reader.GetString();
                }
                else if (propertyName == "reason")
                {
                    if (seenReason || reader.TokenType != JsonTokenType.String)
                        return Rejected("review_parse_failed");
                    seenReason = true;
                    reason = reader.GetString() ?? string.Empty;
                }
                else if (propertyName == "reviewedPartIds")
                {
                    if (seenReviewedPartIds || reader.TokenType != JsonTokenType.StartArray)
                        return Rejected("review_parse_failed");
                    seenReviewedPartIds = true;
                    reviewedPartIds = new List<string>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                            break;
                        if (reader.TokenType != JsonTokenType.String)
                            return Rejected("review_parse_failed");
                        var id = reader.GetString();
                        if (string.IsNullOrWhiteSpace(id))
                            return Rejected("review_parse_failed");
                        reviewedPartIds.Add(id.Trim());
                    }
                }
                else
                {
                    return Rejected("review_parse_failed");
                }
            }

            // Exact one JSON value: no trailing tokens after the object.
            if (reader.Read())
                return Rejected("review_parse_failed");

            if (!seenVerdict || verdict is null || !AllowedVerdicts.Contains(verdict))
                return Rejected("review_parse_failed");
            reason ??= string.Empty;
            if (reason.Length > ContentItemReviewLimits.MaxReasonLength)
                return Rejected("review_parse_failed");

            if (requireReviewedPartIds)
            {
                if (!seenReviewedPartIds || reviewedPartIds is null)
                    return Rejected("review_parse_failed");
            }
            else if (seenReviewedPartIds)
            {
                // Text path rejects unknown/extra fields including reviewedPartIds.
                return Rejected("review_parse_failed");
            }

            return verdict switch
            {
                ContentReviewResult.Approve => new StrictContentReviewOutcome(
                    IsAccepted: true,
                    ReviewStatus: "passed",
                    ReasonCode: "passed",
                    Reason: reason,
                    ErrorCode: null,
                    ReviewedPartIds: reviewedPartIds),
                ContentReviewResult.RejectVerdict => new StrictContentReviewOutcome(
                    IsAccepted: true,
                    ReviewStatus: "rejected",
                    ReasonCode: "agent_non_pass",
                    Reason: reason,
                    ErrorCode: null,
                    ReviewedPartIds: reviewedPartIds),
                ContentReviewResult.NeedsHuman => new StrictContentReviewOutcome(
                    IsAccepted: true,
                    ReviewStatus: "needs_human",
                    ReasonCode: "agent_non_pass",
                    Reason: reason,
                    ErrorCode: null,
                    ReviewedPartIds: reviewedPartIds),
                _ => Rejected("review_parse_failed"),
            };
        }
        catch (JsonException)
        {
            return Rejected("review_parse_failed");
        }
    }

    private static StrictContentReviewOutcome Rejected(string errorCode) =>
        new(
            IsAccepted: false,
            ReviewStatus: "failed",
            ReasonCode: "reviewer_error",
            Reason: null,
            ErrorCode: errorCode);
}
