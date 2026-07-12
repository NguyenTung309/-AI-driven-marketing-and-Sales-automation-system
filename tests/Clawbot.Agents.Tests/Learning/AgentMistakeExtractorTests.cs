using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Learning;

public sealed class AgentMistakeExtractorTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LessonId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task ExtractAsync_parses_add_and_forces_mistake_category()
    {
        var chat = new SequenceChatClient("""
        {"ops":[{"op":"add","factId":null,"fact":"Content hay bịa giá khóa học","category":"profile","confidence":0.9}]}
        """);
        var extractor = new AgentMistakeExtractor(chat, new NoopLlmScope());

        var ops = await extractor.ExtractAsync(TenantId, "reviewer-agent", ["bịa giá 3tr", "giá sai 5tr"], []);

        ops.Should().ContainSingle();
        ops![0].Category.Should().Be("mistake"); // category luôn ép về mistake
    }

    [Fact]
    public async Task ExtractAsync_fabricated_factId_gets_repaired_then_gives_up_null()
    {
        var lessons = new[] { new ContactFact(LessonId, "Lỗi cũ", "mistake", 0.8m) };
        var hopeless = new SequenceChatClient(
            """{"ops":[{"op":"update","factId":"99999999-9999-9999-9999-999999999999","fact":"x","category":"mistake","confidence":0.9}]}""",
            "rác", "rác");
        var extractor = new AgentMistakeExtractor(hopeless, new NoopLlmScope());

        var ops = await extractor.ExtractAsync(TenantId, "reviewer-agent", ["lý do"], lessons);

        ops.Should().BeNull();
        hopeless.Calls.Should().Be(3);
    }

    [Fact]
    public async Task ExtractAsync_empty_reasons_skips_llm()
    {
        var chat = new SequenceChatClient("unused");

        var ops = await new AgentMistakeExtractor(chat, new NoopLlmScope()).ExtractAsync(TenantId, "reviewer-agent", [], []);

        ops.Should().BeEmpty();
        chat.Calls.Should().Be(0);
    }

    private sealed class NoopLlmScope : ILlmCallScope
    {
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
        public LlmCallContext? Current => null;
        public IDisposable Begin(Guid tenantId, string agentCode, DateTimeOffset? costAt = null, Guid? reservationId = null, Guid? sessionId = null) =>
            new NoopDisposable();
    }

    private sealed class SequenceChatClient(params string[] responses) : IClaudeChatClient
    {
        public int Calls { get; private set; }

        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            var response = responses[Math.Min(Calls, responses.Length - 1)];
            Calls++;
            return Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, 1, 1, 0.01m, "test");
        }
    }
}
