using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Rag;
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

    // ai-self-learning-memory Lớp 3: bài học tích lũy nạp vào persona; provider lỗi không chặn review.
    [Fact]
    public async Task Review_injects_agent_memories_into_persona()
    {
        var chat = new CapturingChatClient("""{"verdict":"approve","reason":"ok"}""");
        var provider = new FixedMemoryProvider(["Content hay bịa giá khóa học"]);
        var reviewer = new ContentReviewer(chat, new NoopLlmScope(), memoryProvider: provider);

        await reviewer.ReviewAsync(Guid.NewGuid(), "facebook", "bài đăng");

        chat.SystemPrompt.Should().Contain("Lỗi hay gặp đã tích lũy");
        chat.SystemPrompt.Should().Contain("Content hay bịa giá khóa học");
    }

    [Fact]
    public async Task Review_survives_memory_provider_failure()
    {
        var chat = new CapturingChatClient("""{"verdict":"approve","reason":"ok"}""");
        var reviewer = new ContentReviewer(chat, new NoopLlmScope(), memoryProvider: new ThrowingMemoryProvider());

        var result = await reviewer.ReviewAsync(Guid.NewGuid(), "facebook", "bài đăng");

        result.Verdict.Should().Be(ContentReviewResult.Approve);
        chat.SystemPrompt.Should().NotContain("Lỗi hay gặp đã tích lũy");
    }

    // Fix chính: reviewer đối chiếu số liệu trong bài với KB thay vì chấm mù => "35%" trong KB được đưa vào
    // prompt làm bằng chứng, không còn rơi needs_human oan.
    [Fact]
    public async Task Review_feeds_kb_evidence_into_prompt()
    {
        var chat = new CapturingChatClient("""{"verdict":"approve","reason":"khớp KB"}""");
        var rag = new FixedRagRetriever("0-HSK3 | 86.100.000đ | 35% | 55.965.000đ");
        var reviewer = new ContentReviewer(chat, new NoopLlmScope(), rag: rag);

        var result = await reviewer.ReviewAsync(Guid.NewGuid(), "facebook", "giảm 35% khóa 0-HSK3");

        result.Verdict.Should().Be(ContentReviewResult.Approve);
        chat.UserMessage.Should().Contain("Bằng chứng KB");
        chat.UserMessage.Should().Contain("55.965.000đ");
    }

    [Fact]
    public async Task Review_survives_rag_failure_without_evidence()
    {
        var chat = new CapturingChatClient("""{"verdict":"needs_human","reason":"thiếu đối chiếu"}""");
        var reviewer = new ContentReviewer(chat, new NoopLlmScope(), rag: new ThrowingRagRetriever());

        var result = await reviewer.ReviewAsync(Guid.NewGuid(), "facebook", "giảm 35% khóa 0-HSK3");

        result.Verdict.Should().Be(ContentReviewResult.NeedsHuman);
        chat.UserMessage.Should().NotContain("Bằng chứng KB");
    }

    // RAG tự cap 6s bắn OCE trong khi review chưa bị hủy => phải nuốt, review đi tiếp (không review_unavailable).
    [Fact]
    public async Task Review_swallows_rag_cancellation_when_review_not_cancelled()
    {
        var chat = new CapturingChatClient("""{"verdict":"needs_human","reason":"thiếu đối chiếu"}""");
        var reviewer = new ContentReviewer(chat, new NoopLlmScope(), rag: new CancellingRagRetriever());

        var result = await reviewer.ReviewAsync(Guid.NewGuid(), "facebook", "giảm 35% khóa 0-HSK3");

        result.Verdict.Should().Be(ContentReviewResult.NeedsHuman);
        chat.UserMessage.Should().NotContain("Bằng chứng KB");
    }

    private sealed class FixedRagRetriever(string snippet) : IRagRetriever
    {
        public Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RagChunk>>([new RagChunk("v1", "hoc-phi", snippet, 0.9f)]);
    }

    private sealed class CancellingRagRetriever : IRagRetriever
    {
        public Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<RagChunk>>(new OperationCanceledException());
    }

    private sealed class ThrowingRagRetriever : IRagRetriever
    {
        public Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<RagChunk>>(new InvalidOperationException("qdrant down"));
    }

    private sealed class FixedMemoryProvider(IReadOnlyList<string> facts) : Clawbot.Agents.Core.Learning.IAgentMemoryProvider
    {
        public Task<IReadOnlyList<string>> GetTopFactsAsync(Guid tenantId, string agentCode, int topK, CancellationToken ct = default) =>
            Task.FromResult(facts);
    }

    private sealed class ThrowingMemoryProvider : Clawbot.Agents.Core.Learning.IAgentMemoryProvider
    {
        public Task<IReadOnlyList<string>> GetTopFactsAsync(Guid tenantId, string agentCode, int topK, CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("db down"));
    }

    private sealed class CapturingChatClient(string response) : IClaudeChatClient
    {
        public string? SystemPrompt { get; private set; }
        public string? UserMessage { get; private set; }

        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            SystemPrompt = systemPrompt;
            UserMessage = userMessage;
            return Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, 1, 1, 0.01m, "test");
        }
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
