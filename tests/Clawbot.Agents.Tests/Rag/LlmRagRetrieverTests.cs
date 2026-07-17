using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Rag;

// LLM-mode retrieval: mặc định khi tenant không cấu hình embedding (thay hash-fallback tìm sai âm thầm).
public sealed class LlmRagRetrieverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static LlmRagRetriever BuildSut(
        IReadOnlyList<KbActiveContent> contents,
        string llmReply,
        out IClaudeChatClient claude)
    {
        var reader = Substitute.For<IKbContentReader>();
        reader.GetActiveContentAsync(TenantId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(contents);

        claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(llmReply, 10, 5, 0.001m, "test-model"));

        return new LlmRagRetriever(
            reader, claude, new LlmCallScope(), Substitute.For<ILlmCostTracker>(),
            NullLogger<LlmRagRetriever>.Instance);
    }

    // ChunkContent gom các đoạn ngắn vào chung 1 chunk (~1000 ký tự) — đệm cho mỗi đoạn đủ dài
    // để chắc chắn tách thành các chunk riêng.
    private static string Pad(string text) => text + " " + string.Join(' ', Enumerable.Repeat("nội dung đệm", 60));

    [Fact]
    public async Task Selects_chunks_from_llm_json_envelope()
    {
        // Arrange: 2 đoạn, LLM chọn đoạn 2
        var contents = new[] { new KbActiveContent("v1", "hsk3", $"{Pad("Khóa HSK1 cơ bản.")}\n\n{Pad("Khóa HSK3 học phí 12.300.000đ, 45 buổi.")}") };
        var sut = BuildSut(contents, """{"indexes":[2]}""", out _);

        // Act
        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "học phí hsk3", TopK: 4));

        // Assert
        chunks.Should().ContainSingle();
        chunks[0].Snippet.Should().Contain("12.300.000");
        chunks[0].KbVersionId.Should().Be("v1");
        chunks[0].KbModuleCode.Should().Be("hsk3");
        chunks[0].Score.Should().BeGreaterThan(0.35f, "ChatAgent escalate khi max score < 0.35");
    }

    [Fact]
    public async Task Accepts_bare_json_array_and_caps_at_topk()
    {
        var contents = new[] { new KbActiveContent("v1", "m", $"{Pad("Đoạn một.")}\n\n{Pad("Đoạn hai.")}\n\n{Pad("Đoạn ba.")}") };
        var sut = BuildSut(contents, "[1, 2, 3]", out _);

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "đoạn", TopK: 2));

        chunks.Should().HaveCount(2);
    }

    [Fact]
    public async Task Ignores_out_of_range_indexes()
    {
        var contents = new[] { new KbActiveContent("v1", "m", "Chỉ có một đoạn duy nhất.") };
        var sut = BuildSut(contents, """{"indexes":[0, 1, 99]}""", out _);

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "đoạn", TopK: 4));

        chunks.Should().ContainSingle();
    }

    [Fact]
    public async Task Empty_selection_returns_empty_so_caller_escalates()
    {
        var contents = new[] { new KbActiveContent("v1", "m", "Nội dung về khóa học tiếng Trung.") };
        var sut = BuildSut(contents, """{"indexes":[]}""", out _);

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "hỏi về bảo hiểm xe", TopK: 4));

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_kb_returns_empty_without_calling_llm()
    {
        var sut = BuildSut(Array.Empty<KbActiveContent>(), """{"indexes":[1]}""", out var claude);

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "học phí", TopK: 4));

        chunks.Should().BeEmpty();
        await claude.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Falls_back_to_keyword_ranking_when_llm_reply_is_garbage()
    {
        // Arrange: reply không có JSON -> keyword fallback phải trả đoạn khớp "học phí"
        var contents = new[] { new KbActiveContent("v1", "m", "Lịch khai giảng tháng 8.\n\nHọc phí HSK3 là 12.300.000đ, học phí ưu đãi còn 9.840.000đ.") };
        var sut = BuildSut(contents, "xin lỗi, tôi không hiểu yêu cầu", out _);

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "học phí bao nhiêu", TopK: 4));

        chunks.Should().ContainSingle();
        chunks[0].Snippet.Should().Contain("12.300.000");
        chunks[0].Score.Should().BeGreaterThan(0.35f);
    }

    [Fact]
    public async Task Falls_back_to_keyword_ranking_when_llm_throws()
    {
        var contents = new[] { new KbActiveContent("v1", "m", "Học phí HSK3 là 12.300.000đ.") };
        var reader = Substitute.For<IKbContentReader>();
        reader.GetActiveContentAsync(TenantId, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(contents);
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ClaudeReply>>(_ => throw new InvalidOperationException("llm down"));
        var sut = new LlmRagRetriever(reader, claude, new LlmCallScope(), Substitute.For<ILlmCostTracker>(),
            NullLogger<LlmRagRetriever>.Instance);

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "học phí", TopK: 4));

        chunks.Should().ContainSingle();
        chunks[0].Snippet.Should().Contain("12.300.000");
    }

    [Fact]
    public async Task Records_llm_cost()
    {
        var contents = new[] { new KbActiveContent("v1", "m", "Nội dung khóa học.") };
        var reader = Substitute.For<IKbContentReader>();
        reader.GetActiveContentAsync(TenantId, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(contents);
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("""{"indexes":[1]}""", 10, 5, 0.002m, "test-model"));
        var cost = Substitute.For<ILlmCostTracker>();
        var sut = new LlmRagRetriever(reader, claude, new LlmCallScope(), cost, NullLogger<LlmRagRetriever>.Instance);

        await sut.RetrieveAsync(new RagRequest(TenantId, null, "khóa học", TopK: 4));

        await cost.Received(1).RecordAsync(
            Arg.Is<CostEntry>(e => e.TenantId == TenantId && e.UsdCost == 0.002m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Normalize_strips_vietnamese_diacritics()
    {
        var normalized = LlmRagRetriever.Normalize("Học phí HSK3, ưu đãi 20%! Đăng ký ngay");

        normalized.Should().Contain("hoc phi hsk3");
        normalized.Should().Contain("uu dai 20");
        normalized.Should().Contain("dang ky ngay");
    }

    [Fact]
    public void ParseIndexes_reads_envelope_and_bare_array()
    {
        LlmRagRetriever.ParseIndexes("""Kết quả: {"indexes":[2,5]}""").Should().Equal(2, 5);
        LlmRagRetriever.ParseIndexes("[3,1]").Should().Equal(3, 1);
    }
}

public sealed class RoutingRagRetrieverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (RoutingRagRetriever Sut, IRagRetriever Vector, IKbContentReader Reader, IClaudeChatClient Claude) Build(ResolvedEmbeddingConfig config)
    {
        var resolver = Substitute.For<IEmbeddingConfigResolver>();
        resolver.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(config.IsFallback ? null : config);
        var provider = new ConfiguredEmbeddingProvider(
            [resolver],
            Microsoft.Extensions.Options.Options.Create(new EmbeddingOptions()),
            Microsoft.Extensions.Options.Options.Create(new Clawbot.Agents.Core.Chat.LlmBaseUrlOptions()),
            new TestHostEnvironment(),
            NullLogger<ConfiguredEmbeddingProvider>.Instance);

        var vector = Substitute.For<IRagRetriever>();
        vector.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<RagChunk> { new("v", "m", "vector-hit", 0.8f) });

        var reader = Substitute.For<IKbContentReader>();
        reader.GetActiveContentAsync(TenantId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new KbActiveContent("v1", "m", "Nội dung KB.") });
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("""{"indexes":[1]}""", 1, 1, 0m, "test"));
        var llm = new LlmRagRetriever(reader, claude, new LlmCallScope(), Substitute.For<ILlmCostTracker>(),
            NullLogger<LlmRagRetriever>.Instance);

        var sut = new RoutingRagRetriever(vector, llm, provider, NullLogger<RoutingRagRetriever>.Instance);
        return (sut, vector, reader, claude);
    }

    [Fact]
    public async Task Routes_to_llm_when_no_embedding_config()
    {
        var (sut, vector, reader, _) = Build(new ResolvedEmbeddingConfig("hash", "hash-384", null, null, 384, "hash-fallback"));

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "nội dung", TopK: 4));

        chunks.Should().ContainSingle(c => c.Snippet == "Nội dung KB.");
        await vector.DidNotReceiveWithAnyArgs().RetrieveAsync(default!, default);
        await reader.ReceivedWithAnyArgs(1).GetActiveContentAsync(default, default, default);
    }

    [Fact]
    public async Task Routes_to_vector_when_tenant_config_exists()
    {
        var (sut, vector, reader, _) = Build(new ResolvedEmbeddingConfig("openai", "text-embedding-3-small", "key", null, 1536, "tenant-db"));

        var chunks = await sut.RetrieveAsync(new RagRequest(TenantId, null, "nội dung", TopK: 4));

        chunks.Should().ContainSingle(c => c.Snippet == "vector-hit");
        await reader.DidNotReceiveWithAnyArgs().GetActiveContentAsync(default, default, default);
    }
}
