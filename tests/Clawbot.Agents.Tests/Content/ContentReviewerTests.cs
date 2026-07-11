using Clawbot.Agents.Core.Chat;
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

    // ai-self-learning-memory 1.3b: chấm đề xuất KB — cùng fail-closed skeleton.
    [Fact]
    public async Task ReviewKbSuggestion_returns_parsed_verdict()
    {
        var reviewer = new ContentReviewer(
            new FixedChatClient("""{"verdict":"approve","reason":"khớp bằng chứng"}"""), new NoopLlmScope());

        var result = await reviewer.ReviewKbSuggestionAsync(Guid.NewGuid(), "Học phí", "## 5tr/khóa", "sale xác nhận 5tr");

        result.Verdict.Should().Be(ContentReviewResult.Approve);
    }

    [Fact]
    public async Task ReviewKbSuggestion_llm_error_fails_closed_to_needs_human()
    {
        var reviewer = new ContentReviewer(new ThrowingChatClient(), new NoopLlmScope());

        var result = await reviewer.ReviewKbSuggestionAsync(Guid.NewGuid(), "t", "c", "e");

        result.Verdict.Should().Be(ContentReviewResult.NeedsHuman);
        result.Reason.Should().StartWith("reviewer_unavailable");
    }

    [Fact]
    public async Task ReviewKbSuggestion_empty_content_rejects_without_llm_call()
    {
        var reviewer = new ContentReviewer(new ThrowingChatClient(), new NoopLlmScope());

        var result = await reviewer.ReviewKbSuggestionAsync(Guid.NewGuid(), "t", " ", "e");

        result.Verdict.Should().Be(ContentReviewResult.RejectVerdict);
    }

    private sealed class NoopLlmScope : ILlmCallScope
    {
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
        public LlmCallContext? Current => null;
        public IDisposable Begin(Guid tenantId, string agentCode, DateTimeOffset? costAt = null, Guid? reservationId = null, Guid? sessionId = null) =>
            new NoopDisposable();
    }

    private sealed class FixedChatClient(string response) : IClaudeChatClient
    {
        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"));

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ClaudeStreamChunk(response, Final: true, 1, 1, 0.01m, "test");
        }
    }

    private sealed class ThrowingChatClient : IClaudeChatClient
    {
        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            Task.FromException<ClaudeReply>(new HttpRequestException("gateway down"));

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            throw new HttpRequestException("gateway down");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
