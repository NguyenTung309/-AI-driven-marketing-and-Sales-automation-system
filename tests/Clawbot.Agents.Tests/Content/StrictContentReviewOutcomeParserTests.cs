using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content;

public sealed class StrictContentReviewOutcomeParserTests
{
    // Helper: tao envelope mac dinh hop le
    private static ReviewCompletionEnvelope Env(
        string rawText,
        bool observedTerminalSuccess = true,
        string finishReason = ReviewCompletionFinishReasons.EndTurn,
        bool isRefused = false,
        bool isContentFiltered = false,
        bool isTruncated = false,
        IReadOnlyList<string>? requestedPartIds = null,
        IReadOnlyList<string>? sentPartIds = null)
        => new(
            RawText: rawText,
            ObservedTerminalSuccess: observedTerminalSuccess,
            FinishReason: finishReason,
            IsRefused: isRefused,
            IsContentFiltered: isContentFiltered,
            IsTruncated: isTruncated,
            RequestedPartIds: requestedPartIds ?? [],
            SentPartIds: sentPartIds ?? []);

    private static string Json(string verdict, string reason = "ok", IReadOnlyList<string>? reviewedPartIds = null)
    {
        if (reviewedPartIds is not null)
            return $"{{\"verdict\":\"{verdict}\",\"reason\":\"{reason}\",\"reviewedPartIds\":[{string.Join(",", reviewedPartIds.Select(id => $"\"{id}\""))}]}}";
        return $"{{\"verdict\":\"{verdict}\",\"reason\":\"{reason}\"}}";
    }

    // --- envelope flags fail-closed ---
    [Fact]
    public void Parse_TerminalIncomplete_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), observedTerminalSuccess: false));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_terminal_incomplete");
    }

    [Fact]
    public void Parse_Refused_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), isRefused: true));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_refused");
    }

    [Fact]
    public void Parse_ContentFiltered_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), isContentFiltered: true));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_content_filtered");
    }

    [Fact]
    public void Parse_Truncated_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), isTruncated: true));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_truncated");
    }

    // --- finishReason ---
    [Fact]
    public void Parse_FinishReason_Missing_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), finishReason: ""));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_finish_reason_missing");
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("length")]
    public void Parse_FinishReason_TruncationValues_Rejected(string reason)
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), finishReason: reason));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_truncated");
    }

    [Fact]
    public void Parse_FinishReason_Disallowed_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), finishReason: "tool_use"));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_finish_reason_disallowed");
    }

    [Theory]
    [InlineData("end_turn")]
    [InlineData("stop")]
    public void Parse_FinishReason_Allowed_Accepted(string reason)
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve"), finishReason: reason));
        r.IsAccepted.Should().BeTrue();
    }

    // --- rawText rong ---
    [Fact]
    public void Parse_EmptyOutput_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env("   "));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_empty_output");
    }

    // --- closed-schema JSON ---
    [Theory]
    [InlineData("approve", "passed")]
    [InlineData("reject", "rejected")]
    [InlineData("needs_human", "needs_human")]
    public void Parse_ValidVerdicts_Mapped(string verdict, string expectedStatus)
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json(verdict)));
        r.IsAccepted.Should().BeTrue();
        r.ReviewStatus.Should().Be(expectedStatus);
    }

    [Fact]
    public void Parse_UnknownVerdict_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("unknown_verdict")));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void Parse_ExtraField_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env("{\"verdict\":\"approve\",\"reason\":\"ok\",\"extra\":\"field\"}"));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void Parse_TrailingTokens_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve") + " trailing junk"));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void Parse_TrailingComma_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env("{\"verdict\":\"approve\",\"reason\":\"ok\",}"));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void Parse_JsonComment_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env("{\"verdict\":\"approve\",\"reason\":\"ok\"} // comment"));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void Parse_ReasonTooLong_Rejected()
    {
        var longReason = new string('x', 1025);
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve", longReason)));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void Parse_ReasonExactly1024_Accepted()
    {
        var exactReason = new string('x', 1024);
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve", exactReason)));
        r.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void Parse_NullEnvelope_Throws()
    {
        FluentActions.Invoking(() => StrictContentReviewOutcomeParser.Parse(null!))
            .Should().Throw<ArgumentNullException>();
    }

    // --- text path khong cho reviewedPartIds ---
    [Fact]
    public void Parse_TextPath_WithReviewedPartIds_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.Parse(Env(Json("approve", reviewedPartIds: new[] { "p1" })));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    // --- vision path ---
    [Fact]
    public void ParseVision_ValidWithMatchingPartIds_Accepted()
    {
        var ids = new[] { "p1", "p2" };
        var r = StrictContentReviewOutcomeParser.ParseVision(Env(Json("approve", reviewedPartIds: ids), requestedPartIds: ids, sentPartIds: ids));
        r.IsAccepted.Should().BeTrue();
        r.ReviewedPartIds.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public void ParseVision_MissingReviewedPartIds_Rejected()
    {
        var ids = new[] { "p1" };
        var r = StrictContentReviewOutcomeParser.ParseVision(Env(Json("approve"), requestedPartIds: ids, sentPartIds: ids));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_parse_failed");
    }

    [Fact]
    public void ParseVision_MismatchedRequestedAndSent_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.ParseVision(
            Env(Json("approve", reviewedPartIds: new[] { "p1" }), requestedPartIds: new[] { "p1" }, sentPartIds: new[] { "p2" }));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_part_ids_incomplete");
    }

    [Fact]
    public void ParseVision_ReviewPartIdsIncomplete_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.ParseVision(
            Env(Json("approve", reviewedPartIds: new[] { "p1" }), requestedPartIds: new[] { "p1", "p2" }, sentPartIds: new[] { "p1", "p2" }));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_part_ids_incomplete");
    }

    [Fact]
    public void ParseVision_EmptyPartIds_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.ParseVision(Env(Json("approve", reviewedPartIds: Array.Empty<string>()), requestedPartIds: Array.Empty<string>(), sentPartIds: Array.Empty<string>()));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_part_ids_incomplete");
    }

    [Fact]
    public void ParseVision_DuplicateIds_Rejected()
    {
        var r = StrictContentReviewOutcomeParser.ParseVision(
            Env(Json("approve", reviewedPartIds: new[] { "p1", "p1" }), requestedPartIds: new[] { "p1", "p1" }, sentPartIds: new[] { "p1", "p1" }));
        r.IsAccepted.Should().BeFalse();
        r.ErrorCode.Should().Be("review_part_ids_incomplete");
    }

    // --- ParseLegacyVerdict ---
    [Fact]
    public void ParseLegacyVerdict_Approve_Mapped()
    {
        var r = StrictContentReviewOutcomeParser.ParseLegacyVerdict(Json("approve"));
        r.Verdict.Should().Be(ContentReviewResult.Approve);
    }

    [Fact]
    public void ParseLegacyVerdict_Reject_Mapped()
    {
        var r = StrictContentReviewOutcomeParser.ParseLegacyVerdict(Json("reject"));
        r.Verdict.Should().Be(ContentReviewResult.RejectVerdict);
    }

    [Fact]
    public void ParseLegacyVerdict_ParseFailed_ReturnsNeedsHuman()
    {
        var r = StrictContentReviewOutcomeParser.ParseLegacyVerdict("not json at all");
        r.Verdict.Should().Be(ContentReviewResult.NeedsHuman);
        r.Reason.Should().Contain("review_parse_failed");
    }

    // --- ValidatePartIdCompleteness truc tiep ---
    [Fact]
    public void ValidatePartIdCompleteness_Matching_ReturnsNull()
    {
        StrictContentReviewOutcomeParser.ValidatePartIdCompleteness(new[] { "a", "b" }, new[] { "b", "a" }, new[] { "a", "b" })
            .Should().BeNull();
    }

    [Fact]
    public void ValidatePartIdCompleteness_RequestedSentMismatch_ReturnsError()
    {
        StrictContentReviewOutcomeParser.ValidatePartIdCompleteness(new[] { "a" }, new[] { "b" }, new[] { "a" })
            .Should().Be("review_part_ids_incomplete");
    }
}
