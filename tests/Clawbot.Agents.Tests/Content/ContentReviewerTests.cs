using Clawbot.Agents.Core.Content;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Content;

// Review-gate P1: verdict parsing is FAIL-CLOSED — anything unparseable lands on needs_human, never approve.
public sealed class ContentReviewerTests
{
    [Fact]
    public void Parse_approve_verdict()
    {
        var result = ContentReviewer.Parse("""{"verdict":"approve","reason":"đạt cả 5 tiêu chí"}""");
        result.Verdict.Should().Be(ContentReviewResult.Approve);
        result.Reason.Should().Be("đạt cả 5 tiêu chí");
    }

    [Fact]
    public void Parse_reject_verdict()
    {
        var result = ContentReviewer.Parse("""{"verdict":"reject","reason":"bịa giá"}""");
        result.Verdict.Should().Be(ContentReviewResult.RejectVerdict);
    }

    [Fact]
    public void Parse_tolerates_prose_around_json()
    {
        var result = ContentReviewer.Parse("Đây là kết quả: {\"verdict\":\"approve\",\"reason\":\"ok\"} — hết.");
        result.Verdict.Should().Be(ContentReviewResult.Approve);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"verdict":"maybe","reason":"?"}""")]
    [InlineData("""{"verdict":"APPROVE_ALL"}""")]
    public void Parse_fails_closed_to_needs_human(string text)
    {
        var result = ContentReviewer.Parse(text);
        result.Verdict.Should().Be(ContentReviewResult.NeedsHuman);
    }
}
