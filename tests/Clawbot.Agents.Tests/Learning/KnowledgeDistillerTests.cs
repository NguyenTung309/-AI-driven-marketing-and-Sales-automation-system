using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Learning;

public sealed class KnowledgeDistillerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ModuleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly DistillSignal Signal = new(
        "ai_failed", "Học phí HSK4 bao nhiêu?", "Xin lỗi, tôi chưa có thông tin.", "5 triệu/khóa 3 tháng em nhé.");

    [Fact]
    public async Task DistillAsync_parses_valid_draft()
    {
        var chat = new SequenceChatClient("""
        {"title":"Học phí HSK4","contentMd":"## Học phí\n5 triệu/khóa 3 tháng","rationale":"AI trượt câu này","normalizedQuestion":"học phí hsk4"}
        """);
        var distiller = new KnowledgeDistiller(chat, new NoopLlmScope());

        var draft = await distiller.DistillAsync(TenantId, [Signal]);

        draft.Should().NotBeNull();
        draft!.Title.Should().Be("Học phí HSK4");
        draft.NormalizedQuestion.Should().Be("học phí hsk4");
        chat.Calls.Should().Be(1);
    }

    [Fact]
    public async Task DistillAsync_self_repairs_then_gives_up_null()
    {
        // Lần 1 rác -> feedback lỗi -> lần 2 hợp lệ. Hết 3 lượt vẫn rác -> null (job skip, không ghi dữ liệu đoán).
        var repaired = new SequenceChatClient(
            "xin lỗi, tôi không thể",
            """{"title":"t","contentMd":"c","rationale":"r","normalizedQuestion":"q"}""");
        var hopeless = new SequenceChatClient("rác 1", "rác 2", "rác 3");
        var distiller1 = new KnowledgeDistiller(repaired, new NoopLlmScope());
        var distiller2 = new KnowledgeDistiller(hopeless, new NoopLlmScope());

        var ok = await distiller1.DistillAsync(TenantId, [Signal]);
        var dead = await distiller2.DistillAsync(TenantId, [Signal]);

        ok.Should().NotBeNull();
        repaired.Calls.Should().Be(2);
        repaired.LastUserMessage.Should().Contain("không hợp lệ");
        dead.Should().BeNull();
        hopeless.Calls.Should().Be(3);
    }

    [Fact]
    public async Task DistillAsync_empty_cluster_returns_null_without_llm_call()
    {
        var chat = new SequenceChatClient("unused");

        var draft = await new KnowledgeDistiller(chat, new NoopLlmScope()).DistillAsync(TenantId, []);

        draft.Should().BeNull();
        chat.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ConsolidateAsync_empty_kb_short_circuits_to_add()
    {
        var chat = new SequenceChatClient("unused");
        var draft = new KbSuggestionDraft("t", "c", "r", "q");

        var result = await new KnowledgeDistiller(chat, new NoopLlmScope()).ConsolidateAsync(TenantId, draft, []);

        result.Should().Be(new ConsolidationResult("add", null, null));
        chat.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ConsolidateAsync_update_requires_target_module_and_repairs()
    {
        // update thiếu targetModuleId là KQ hỏng -> tự sửa ở lượt sau.
        var chat = new SequenceChatClient(
            """{"op":"update","targetModuleId":null,"mergedContentMd":"x"}""",
            $$"""{"op":"update","targetModuleId":"{{ModuleId}}","mergedContentMd":"## Gộp"}""");
        var draft = new KbSuggestionDraft("t", "c", "r", "q");
        var modules = new[] { new ExistingKbModule(ModuleId, "hoc-phi", "Học phí", "5tr/khóa") };

        var result = await new KnowledgeDistiller(chat, new NoopLlmScope()).ConsolidateAsync(TenantId, draft, modules);

        result.Should().NotBeNull();
        result!.Op.Should().Be("update");
        result.TargetModuleId.Should().Be(ModuleId);
        chat.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ConsolidateAsync_accepts_noop()
    {
        var chat = new SequenceChatClient("""{"op":"noop","targetModuleId":null,"mergedContentMd":null}""");
        var draft = new KbSuggestionDraft("t", "c", "r", "q");
        var modules = new[] { new ExistingKbModule(ModuleId, "hoc-phi", "Học phí", "đã có đủ") };

        var result = await new KnowledgeDistiller(chat, new NoopLlmScope()).ConsolidateAsync(TenantId, draft, modules);

        result!.Op.Should().Be("noop");
    }

    [Fact]
    public void ComputeDedupHash_normalizes_case_punctuation_whitespace()
    {
        var a = KnowledgeDistiller.ComputeDedupHash("Học phí HSK4?");
        var b = KnowledgeDistiller.ComputeDedupHash("  học phí   hsk4 ");
        var c = KnowledgeDistiller.ComputeDedupHash("lịch khai giảng");

        a.Should().Be(b);
        a.Should().NotBe(c);
        a.Should().MatchRegex("^[0-9a-f]{64}$");
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
        public string? LastUserMessage { get; private set; }

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            LastUserMessage = userMessage;
            var response = responses[Math.Min(Calls, responses.Length - 1)];
            Calls++;
            return Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct);
            yield return new ClaudeStreamChunk(reply.Text, Final: false, 0, 0, 0m);
            yield return new ClaudeStreamChunk(string.Empty, Final: true, 1, 1, 0.01m, "test");
        }
    }
}
