using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Learning;

public sealed class ContactFactExtractorTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ExistingFactId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static readonly ContactFact Existing = new(ExistingFactId, "Thích ca tối 2-4-6", "preference", 0.8m);

    [Fact]
    public async Task ExtractAsync_parses_add_ops_and_defaults()
    {
        var chat = new SequenceChatClient("""
        {"ops":[{"op":"add","factId":null,"fact":"Học viên trình độ HSK3","category":"profile","confidence":0.9},
                {"op":"add","factId":null,"fact":"Muốn học cấp tốc","category":"tâm trạng","confidence":null}]}
        """);
        var extractor = new ContactFactExtractor(chat, new NoopLlmScope());

        var ops = await extractor.ExtractAsync(TenantId, "khách: em học HSK3 rồi", []);

        ops.Should().HaveCount(2);
        ops![0].Fact.Should().Be("Học viên trình độ HSK3");
        ops[1].Category.Should().Be("profile"); // category lạ rơi về profile
        ops[1].Confidence.Should().Be(0.7m);    // confidence thiếu -> mặc định
    }

    [Fact]
    public async Task ExtractAsync_update_with_fabricated_factId_gets_repaired()
    {
        // Model bịa factId -> batch coi như hỏng -> feedback -> lượt 2 trỏ đúng id.
        var chat = new SequenceChatClient(
            """{"ops":[{"op":"update","factId":"99999999-9999-9999-9999-999999999999","fact":"Đổi ca tối 3-5-7","category":"preference","confidence":0.9}]}""",
            $$"""{"ops":[{"op":"update","factId":"{{ExistingFactId}}","fact":"Đổi ca tối 3-5-7","category":"preference","confidence":0.9}]}""");
        var extractor = new ContactFactExtractor(chat, new NoopLlmScope());

        var ops = await extractor.ExtractAsync(TenantId, "khách: em đổi sang ca 3-5-7 nhé", [Existing]);

        ops.Should().ContainSingle();
        ops![0].FactId.Should().Be(ExistingFactId);
        chat.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ExtractAsync_noop_ops_are_dropped_and_empty_ok()
    {
        var chat = new SequenceChatClient("""{"ops":[{"op":"noop","factId":null,"fact":null,"category":null,"confidence":null}]}""");
        var extractor = new ContactFactExtractor(chat, new NoopLlmScope());

        var ops = await extractor.ExtractAsync(TenantId, "khách: chào em", [Existing]);

        ops.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAsync_gives_up_null_after_three_bad_replies()
    {
        var chat = new SequenceChatClient("rác", "rác", "rác");
        var extractor = new ContactFactExtractor(chat, new NoopLlmScope());

        var ops = await extractor.ExtractAsync(TenantId, "khách: hỏi gì đó", [Existing]);

        ops.Should().BeNull();
        chat.Calls.Should().Be(3);
    }

    [Fact]
    public async Task ExtractAsync_empty_transcript_returns_empty_without_llm()
    {
        var chat = new SequenceChatClient("unused");

        var ops = await new ContactFactExtractor(chat, new NoopLlmScope()).ExtractAsync(TenantId, " ", []);

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
