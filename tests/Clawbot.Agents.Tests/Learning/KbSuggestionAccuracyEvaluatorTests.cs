using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Rag;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Learning;

// Khoá lại fix check-implementation #1: "sau" = proposed ĐỨNG MỘT MÌNH (replace), KHÔNG nối contextBefore.
// Nếu append thì proposed kém vẫn cho after >= before -> rail vô nghĩa. Test chứng minh after phản ánh
// đúng proposed: proposed thiếu thông tin -> accuracy_after < before -> rail chặn được.
public sealed class KbSuggestionAccuracyEvaluatorTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly KbAccuracyCase[] Cases =
    [
        new("Câu hỏi topic=fee", "5 triệu"),
        new("Câu hỏi topic=schedule", "đầu tháng"),
    ];

    [Fact]
    public async Task No_test_cases_returns_null_pair()
    {
        var evaluator = new KbSuggestionAccuracyEvaluator(
            new KeywordRag("cũ"), new KeywordJudge(), new NoopLlmScope());

        var pair = await evaluator.EvaluateAsync(TenantId, "hoc-phi", [], "nội dung mới", CancellationToken.None);

        pair.Before.Should().BeNull();
        pair.After.Should().BeNull();
    }

    [Fact]
    public async Task Weaker_proposal_scores_lower_after_than_before_so_rail_can_block()
    {
        // Module hiện tại (RAG) trả lời được CẢ 2 câu; proposed chỉ chứa "học phí" -> mất câu lịch khai giảng.
        // Nếu code còn append (before+proposed) thì after = 100% (>= before) và rail sẽ mù. Với replace,
        // after < before -> rail chặn đúng.
        var rag = new KeywordRag("fee schedule"); // context hiện tại cover cả 2 topic
        var judge = new KeywordJudge();
        var evaluator = new KbSuggestionAccuracyEvaluator(rag, judge, new NoopLlmScope());

        var pair = await evaluator.EvaluateAsync(TenantId, "hoc-phi", Cases, "fee", CancellationToken.None);

        pair.Before.Should().Be(100m);
        pair.After.Should().Be(50m); // proposed chỉ cover 1/2 case (fee)
    }

    [Fact]
    public async Task Op_add_empty_before_measures_proposed_alone()
    {
        var rag = new KeywordRag(); // module mới -> RAG rỗng
        var evaluator = new KbSuggestionAccuracyEvaluator(rag, new KeywordJudge(), new NoopLlmScope());

        var pair = await evaluator.EvaluateAsync(TenantId, "moi", Cases, "fee schedule", CancellationToken.None);

        pair.Before.Should().Be(0m);
        pair.After.Should().Be(100m);
    }

    // RAG trả 1 chunk chứa các topic-token cấu hình (mô phỏng nội dung module hiện tại).
    private sealed class KeywordRag(string topics = "") : IRagRetriever
    {
        public Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
        {
            IReadOnlyList<RagChunk> chunks = string.IsNullOrEmpty(topics)
                ? []
                : [new RagChunk("v1", request.KbModuleCode ?? "m", topics, 0.9f)];
            return Task.FromResult(chunks);
        }
    }

    // Judge deterministic: câu hỏi mang topic=<t>; pass khi phần Context (trước dòng "Question:")
    // chứa token <t>. Tách context khỏi question nên context và question không lẫn.
    private sealed class KeywordJudge : IClaudeChatClient
    {
        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            var questionIdx = userMessage.IndexOf("Question:", StringComparison.Ordinal);
            var context = questionIdx > 0 ? userMessage[..questionIdx] : userMessage;
            var question = questionIdx > 0 ? userMessage[questionIdx..] : "";
            var topicIdx = question.IndexOf("topic=", StringComparison.Ordinal);
            var topic = topicIdx >= 0 ? question[(topicIdx + 6)..].Split([' ', '\n', '\r'])[0] : "";
            var passed = topic.Length > 0 && context.Contains(topic, StringComparison.Ordinal);
            return Task.FromResult(new ClaudeReply(passed ? """{"passed":true}""" : """{"passed":false}""", 1, 1, 0m, "test"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, 1, 1, 0m, "test");
        }
    }

    private sealed class NoopLlmScope : ILlmCallScope
    {
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
        public LlmCallContext? Current => null;
        public IDisposable Begin(Guid tenantId, string agentCode, DateTimeOffset? costAt = null, Guid? reservationId = null, Guid? sessionId = null) =>
            new NoopDisposable();
    }
}
