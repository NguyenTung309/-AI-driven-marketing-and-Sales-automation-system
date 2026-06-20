using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class KbTestRunnerServiceTests
{
    [Fact]
    public void ParseClaudeEvaluation_accepts_json_with_spacing_and_reason()
    {
        var result = KbTestRunnerService.ParseClaudeEvaluation(
            """{ "passed": true, "reason": "Context supports the expected answer." }""");

        result.Passed.Should().BeTrue();
        result.Reason.Should().Be("Context supports the expected answer.");
    }

    [Fact]
    public async Task EvaluateAsync_uses_rag_and_claude_reason_as_case_answer()
    {
        var tenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var moduleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var rag = new CapturingRagRetriever(new[]
        {
            new RagChunk("v1", "HSK", "HSK3 tuition is 3,000,000 VND.", 0.91f),
        });
        var claude = new CapturingClaude("""{ "passed": true, "reason": "Tuition matches the expected answer." }""");
        var sut = new KbTestRunnerService(rag, claude, new LlmCallScope());
        var testCase = KbTestCase.Create(moduleId, "HSK3 hoc phi bao nhieu?", "3,000,000 VND", DateTimeOffset.UtcNow);

        var result = await sut.EvaluateAsync(tenantId, "HSK", testCase, CancellationToken.None);

        result.TestCaseId.Should().Be(testCase.Id);
        result.Question.Should().Be(testCase.Question);
        result.Passed.Should().BeTrue();
        result.Answer.Should().Be("Tuition matches the expected answer.");
        rag.CapturedRequest.Should().Be(new RagRequest(tenantId, "HSK", testCase.Question, 3));
        claude.CapturedPrompt.Should().Contain("HSK3 tuition is 3,000,000 VND.");
        claude.CapturedPrompt.Should().Contain(testCase.Question);
        claude.CapturedPrompt.Should().Contain(testCase.ExpectedAnswer);
    }

    private sealed class CapturingRagRetriever(IReadOnlyList<RagChunk> chunks) : IRagRetriever
    {
        public RagRequest? CapturedRequest { get; private set; }

        public Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
        {
            CapturedRequest = request;
            return Task.FromResult(chunks);
        }
    }

    private sealed class CapturingClaude(string response) : IClaudeChatClient
    {
        public string? CapturedPrompt { get; private set; }

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            _ = systemPrompt;
            _ = history;
            CapturedPrompt = userMessage;
            return Task.FromResult(new ClaudeReply(response, 11, 7, 0.000138m));
        }

        public IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            _ = systemPrompt;
            _ = history;
            _ = userMessage;
            _ = ct;
            return EmptyStream();
        }

        private static async IAsyncEnumerable<ClaudeStreamChunk> EmptyStream()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
