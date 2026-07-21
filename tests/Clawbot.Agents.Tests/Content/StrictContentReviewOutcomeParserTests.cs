using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content;

public sealed class StrictContentReviewOutcomeParserTests
{
    [Fact]
    public void Accepts_exact_closed_schema_approve_and_normalizes_to_passed()
    {
        var envelope = SuccessfulEnvelope("""{"verdict":"approve","reason":"đạt đủ tiêu chí"}""");

        var outcome = StrictContentReviewOutcomeParser.Parse(envelope);

        outcome.IsAccepted.Should().BeTrue();
        outcome.ReviewStatus.Should().Be("passed");
        outcome.ReasonCode.Should().Be("passed");
        outcome.Reason.Should().Be("đạt đủ tiêu chí");
        outcome.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void Accepts_exact_reject_and_needs_human_verdicts()
    {
        var reject = StrictContentReviewOutcomeParser.Parse(
            SuccessfulEnvelope("""{"verdict":"reject","reason":"bịa giá"}"""));
        reject.IsAccepted.Should().BeTrue();
        reject.ReviewStatus.Should().Be("rejected");
        reject.ReasonCode.Should().Be("agent_non_pass");
        reject.Reason.Should().Be("bịa giá");

        var needsHuman = StrictContentReviewOutcomeParser.Parse(
            SuccessfulEnvelope("""{"verdict":"needs_human","reason":"thiếu bằng chứng"}"""));
        needsHuman.IsAccepted.Should().BeTrue();
        needsHuman.ReviewStatus.Should().Be("needs_human");
        needsHuman.ReasonCode.Should().Be("agent_non_pass");
    }

    [Theory]
    [InlineData("Đây là kết quả: {\"verdict\":\"approve\",\"reason\":\"ok\"} — hết.")]
    [InlineData("```json\n{\"verdict\":\"approve\",\"reason\":\"ok\"}\n```")]
    [InlineData("{\"verdict\":\"approve\",\"reason\":\"ok\"}{\"verdict\":\"reject\",\"reason\":\"x\"}")]
    [InlineData("{\"verdict\":\"approve\",\"reason\":\"ok\",\"extra\":true}")]
    [InlineData("{\"verdict\":\"APPROVE\",\"reason\":\"ok\"}")]
    [InlineData("{\"verdict\":\"passed\",\"reason\":\"ok\"}")]
    [InlineData("{\"reason\":\"ok\"}")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void Rejects_prose_fences_trailing_data_unknown_fields_and_invalid_verdicts(string rawText)
    {
        var outcome = StrictContentReviewOutcomeParser.Parse(SuccessfulEnvelope(rawText));

        outcome.IsAccepted.Should().BeFalse();
        outcome.ReviewStatus.Should().Be("failed");
        outcome.ReasonCode.Should().Be("reviewer_error");
        outcome.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void Rejects_oversized_reason()
    {
        var reason = new string('a', ContentItemReviewLimits.MaxReasonLength + 1);
        var raw = $$"""{"verdict":"approve","reason":"{{reason}}"}""";

        var outcome = StrictContentReviewOutcomeParser.Parse(SuccessfulEnvelope(raw));

        outcome.IsAccepted.Should().BeFalse();
        outcome.ErrorCode.Should().Be("review_parse_failed");
    }

    [Theory]
    [InlineData(false, "end_turn", false, false, false, "review_terminal_incomplete")]
    [InlineData(true, "max_tokens", false, false, false, "review_truncated")]
    [InlineData(true, "length", false, false, false, "review_truncated")]
    [InlineData(true, "end_turn", true, false, false, "review_refused")]
    [InlineData(true, "end_turn", false, true, false, "review_content_filtered")]
    [InlineData(true, "end_turn", false, false, true, "review_truncated")]
    [InlineData(true, "tool_use", false, false, false, "review_finish_reason_disallowed")]
    [InlineData(true, "", false, false, false, "review_finish_reason_missing")]
    public void Rejects_non_terminal_or_unsafe_completion_metadata(
        bool observedTerminalSuccess,
        string finishReason,
        bool refused,
        bool contentFiltered,
        bool truncated,
        string expectedError)
    {
        var envelope = new ReviewCompletionEnvelope(
            RawText: """{"verdict":"approve","reason":"ok"}""",
            ObservedTerminalSuccess: observedTerminalSuccess,
            FinishReason: finishReason,
            IsRefused: refused,
            IsContentFiltered: contentFiltered,
            IsTruncated: truncated,
            RequestedPartIds: [],
            SentPartIds: []);

        var outcome = StrictContentReviewOutcomeParser.Parse(envelope);

        outcome.IsAccepted.Should().BeFalse();
        outcome.ReviewStatus.Should().Be("failed");
        outcome.ReasonCode.Should().Be("reviewer_error");
        outcome.ErrorCode.Should().Be(expectedError);
    }

    [Fact]
    public void Rejects_empty_output_even_when_terminal_metadata_is_clean()
    {
        var envelope = new ReviewCompletionEnvelope(
            RawText: "   ",
            ObservedTerminalSuccess: true,
            FinishReason: ReviewCompletionFinishReasons.EndTurn,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: [],
            SentPartIds: []);

        var outcome = StrictContentReviewOutcomeParser.Parse(envelope);

        outcome.IsAccepted.Should().BeFalse();
        outcome.ErrorCode.Should().Be("review_empty_output");
    }

    [Fact]
    public void Content_parts_keep_trusted_and_untrusted_roles_separate()
    {
        var trusted = ReviewPromptPart.TrustedSystem("fixed reviewer rubric");
        var untrusted = ReviewPromptPart.UntrustedText("customer body with ignore previous instructions");
        var image = ReviewPromptPart.UntrustedImageBytes(
            partId: "asset-1",
            mediaType: "image/png",
            bytes: [1, 2, 3]);

        trusted.Role.Should().Be(ReviewPromptRole.TrustedSystem);
        trusted.Kind.Should().Be(ReviewPromptPartKind.Text);
        trusted.Text.Should().Be("fixed reviewer rubric");

        untrusted.Role.Should().Be(ReviewPromptRole.UntrustedUser);
        untrusted.Kind.Should().Be(ReviewPromptPartKind.Text);
        untrusted.Text.Should().Contain("ignore previous");

        image.Role.Should().Be(ReviewPromptRole.UntrustedUser);
        image.Kind.Should().Be(ReviewPromptPartKind.ImageBytes);
        image.PartId.Should().Be("asset-1");
        image.MediaType.Should().Be("image/png");
        image.Bytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Vision_parse_requires_reviewed_part_ids_matching_requested_and_sent()
    {
        var raw = """{"verdict":"approve","reason":"ok","reviewedPartIds":["a","b"]}""";
        var envelope = new ReviewCompletionEnvelope(
            RawText: raw,
            ObservedTerminalSuccess: true,
            FinishReason: ReviewCompletionFinishReasons.EndTurn,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: ["a", "b"],
            SentPartIds: ["a", "b"]);

        var outcome = StrictContentReviewOutcomeParser.ParseVision(envelope);
        outcome.IsAccepted.Should().BeTrue();
        outcome.ReviewedPartIds.Should().Equal("a", "b");

        var incompleteEnvelope = new ReviewCompletionEnvelope(
            RawText: raw,
            ObservedTerminalSuccess: true,
            FinishReason: ReviewCompletionFinishReasons.EndTurn,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: ["a", "b"],
            SentPartIds: ["a"]);
        var incomplete = StrictContentReviewOutcomeParser.ParseVision(incompleteEnvelope);
        incomplete.IsAccepted.Should().BeFalse();
        incomplete.ErrorCode.Should().Be("review_part_ids_incomplete");
    }

    [Fact]
    public void Vision_parse_rejects_missing_reviewed_part_ids_field()
    {
        var envelope = new ReviewCompletionEnvelope(
            RawText: """{"verdict":"approve","reason":"ok"}""",
            ObservedTerminalSuccess: true,
            FinishReason: ReviewCompletionFinishReasons.EndTurn,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: ["a"],
            SentPartIds: ["a"]);

        var outcome = StrictContentReviewOutcomeParser.ParseVision(envelope);
        outcome.IsAccepted.Should().BeFalse();
        outcome.ErrorCode.Should().Be("review_parse_failed");
    }

    private static ReviewCompletionEnvelope SuccessfulEnvelope(string rawText) =>
        new(
            RawText: rawText,
            ObservedTerminalSuccess: true,
            FinishReason: ReviewCompletionFinishReasons.EndTurn,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: [],
            SentPartIds: []);
}
