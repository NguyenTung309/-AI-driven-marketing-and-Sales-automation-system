using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Learning;

// Chưng cất tri thức từ hội thoại thật: distill 1 cụm -> draft, consolidate với KB hiện có (add/update/merge/noop),
// đề xuất cặp gộp (validate id tồn tại + không tự gộp), gộp nội dung đầy đủ. LLM chập chờn => self-repair <=3.
public sealed class KnowledgeDistillerTests
{
    private static readonly Guid Tenant = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static (KnowledgeDistiller Distiller, IClaudeChatClient Claude) NewDistiller(params string[] replies)
    {
        var claude = Substitute.For<IClaudeChatClient>();
        var scope = Substitute.For<ILlmCallScope>();
        scope.Begin(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns(new NoopDisposable());

        var queued = replies.Select(r => new ClaudeReply(r, 0, 0, 0m)).ToArray();
        if (queued.Length == 1)
            claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(queued[0]);
        else if (queued.Length > 1)
            claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(queued[0], queued[1..]);

        return (new KnowledgeDistiller(claude, scope), claude);
    }

    private static ExistingKbModule Module(string name, string excerpt) =>
        new(Guid.NewGuid(), "code-" + name, name, excerpt);

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    // ---- DistillAsync ----

    [Fact]
    public async Task Distill_EmptyCluster_ReturnsNullWithoutCallingLlm()
    {
        var (distiller, claude) = NewDistiller("unused");

        var result = await distiller.DistillAsync(Tenant, Array.Empty<DistillSignal>());

        result.Should().BeNull();
        await claude.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Distill_WellFormed_ReturnsDraft()
    {
        var json = """{"title":"Học phí HSK4","contentMd":"## Học phí\nHSK4: 3.000.000đ","rationale":"khách hỏi nhiều","normalizedQuestion":"hoc phi hsk4"}""";
        var (distiller, _) = NewDistiller(json);
        var cluster = new[] { new DistillSignal("ai_failed", "Học phí HSK4 bao nhiêu?", "Tôi không rõ", "3 triệu") };

        var result = await distiller.DistillAsync(Tenant, cluster);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Học phí HSK4");
        result.NormalizedQuestion.Should().Be("hoc phi hsk4");
    }

    [Fact]
    public async Task Distill_MissingRequiredField_RetriesThenNull()
    {
        // Thiếu normalizedQuestion ở cả 3 lượt -> parse null -> hết attempt -> null.
        var bad = """{"title":"T","contentMd":"C","rationale":"R","normalizedQuestion":""}""";
        var (distiller, claude) = NewDistiller(bad, bad, bad);
        var cluster = new[] { new DistillSignal("repeated_question", "câu hỏi", null, null) };

        var result = await distiller.DistillAsync(Tenant, cluster);

        result.Should().BeNull();
        await claude.Received(3).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Distill_RecoversOnSecondAttempt()
    {
        var bad = """{"title":"","contentMd":"","rationale":"","normalizedQuestion":""}""";
        var good = """{"title":"OK","contentMd":"nội dung","rationale":"lý do","normalizedQuestion":"ok"}""";
        var (distiller, claude) = NewDistiller(bad, good);
        var cluster = new[] { new DistillSignal("sale_answered", "hỏi", null, "đáp") };

        var result = await distiller.DistillAsync(Tenant, cluster);

        result.Should().NotBeNull();
        result!.Title.Should().Be("OK");
        await claude.Received(2).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- ConsolidateAsync ----

    [Fact]
    public async Task Consolidate_NoExistingModules_ReturnsAddWithoutCallingLlm()
    {
        var (distiller, claude) = NewDistiller("unused");
        var draft = new KbSuggestionDraft("T", "C", "R", "q");

        var result = await distiller.ConsolidateAsync(Tenant, draft, Array.Empty<ExistingKbModule>());

        result.Should().NotBeNull();
        result!.Op.Should().Be("add");
        await claude.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consolidate_NoopOp_Accepted()
    {
        var json = """{"op":"noop","targetModuleId":null,"mergedContentMd":null}""";
        var (distiller, _) = NewDistiller(json);
        var draft = new KbSuggestionDraft("T", "C", "R", "q");

        var result = await distiller.ConsolidateAsync(Tenant, draft, new[] { Module("A", "trích A") });

        result!.Op.Should().Be("noop");
        result.TargetModuleId.Should().BeNull();
    }

    [Fact]
    public async Task Consolidate_UpdateWithTarget_Accepted()
    {
        var target = Guid.NewGuid();
        var json = $$"""{"op":"update","targetModuleId":"{{target}}","mergedContentMd":"bản gộp"}""";
        var (distiller, _) = NewDistiller(json);
        var draft = new KbSuggestionDraft("T", "C", "R", "q");

        var result = await distiller.ConsolidateAsync(Tenant, draft, new[] { Module("A", "trích A") });

        result!.Op.Should().Be("update");
        result.TargetModuleId.Should().Be(target);
        result.MergedContentMd.Should().Be("bản gộp");
    }

    [Fact]
    public async Task Consolidate_UpdateMissingTarget_RetriesThenNull()
    {
        // update nhưng thiếu targetModuleId -> invalid ở cả 3 lượt -> null.
        var bad = """{"op":"update","targetModuleId":null,"mergedContentMd":"x"}""";
        var (distiller, claude) = NewDistiller(bad, bad, bad);
        var draft = new KbSuggestionDraft("T", "C", "R", "q");

        var result = await distiller.ConsolidateAsync(Tenant, draft, new[] { Module("A", "trích A") });

        result.Should().BeNull();
        await claude.Received(3).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consolidate_UnknownOp_RetriesThenNull()
    {
        var bad = """{"op":"delete","targetModuleId":null,"mergedContentMd":null}""";
        var (distiller, _) = NewDistiller(bad, bad, bad);
        var draft = new KbSuggestionDraft("T", "C", "R", "q");

        var result = await distiller.ConsolidateAsync(Tenant, draft, new[] { Module("A", "trích A") });

        result.Should().BeNull();
    }

    // ---- ProposeMergesAsync ----

    [Fact]
    public async Task ProposeMerges_FewerThanTwoModules_ReturnsEmptyWithoutCallingLlm()
    {
        var (distiller, claude) = NewDistiller("unused");

        var result = await distiller.ProposeMergesAsync(Tenant, new[] { Module("A", "x") });

        result.Should().BeEmpty();
        await claude.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProposeMerges_ValidPair_Accepted()
    {
        var a = Module("A", "trích A");
        var b = Module("B", "trích B");
        var json = $$"""{"merges":[{"targetModuleId":"{{a.Id}}","sourceModuleId":"{{b.Id}}","reason":"cùng chủ đề"}]}""";
        var (distiller, _) = NewDistiller(json);

        var result = await distiller.ProposeMergesAsync(Tenant, new[] { a, b });

        result.Should().ContainSingle();
        result![0].TargetModuleId.Should().Be(a.Id);
        result[0].SourceModuleId.Should().Be(b.Id);
    }

    [Fact]
    public async Task ProposeMerges_EmptyMerges_ReturnsEmpty()
    {
        var a = Module("A", "x");
        var b = Module("B", "y");
        var (distiller, _) = NewDistiller("""{"merges":[]}""");

        var result = await distiller.ProposeMergesAsync(Tenant, new[] { a, b });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeMerges_SelfMerge_RetriesThenNull()
    {
        var a = Module("A", "x");
        var b = Module("B", "y");
        // Tự gộp chính mình -> invalid -> hết attempt -> null.
        var bad = $$"""{"merges":[{"targetModuleId":"{{a.Id}}","sourceModuleId":"{{a.Id}}","reason":"lỗi"}]}""";
        var (distiller, claude) = NewDistiller(bad, bad, bad);

        var result = await distiller.ProposeMergesAsync(Tenant, new[] { a, b });

        result.Should().BeNull();
        await claude.Received(3).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProposeMerges_UnknownModuleId_RetriesThenNull()
    {
        var a = Module("A", "x");
        var b = Module("B", "y");
        var ghost = Guid.NewGuid();
        var bad = $$"""{"merges":[{"targetModuleId":"{{a.Id}}","sourceModuleId":"{{ghost}}","reason":"id bịa"}]}""";
        var (distiller, _) = NewDistiller(bad, bad, bad);

        var result = await distiller.ProposeMergesAsync(Tenant, new[] { a, b });

        result.Should().BeNull();
    }

    // ---- MergeModulesAsync ----

    [Fact]
    public async Task MergeModules_WellFormed_ReturnsMergedDraft()
    {
        var json = """{"title":"Gộp A+B","contentMd":"nội dung gộp","rationale":"trùng","normalizedQuestion":"merge a b"}""";
        var (distiller, _) = NewDistiller(json);

        var result = await distiller.MergeModulesAsync(Tenant, "A", "nội dung A", "B", "nội dung B");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Gộp A+B");
        result.ContentMd.Should().Be("nội dung gộp");
    }

    [Fact]
    public async Task MergeModules_MissingFields_RetriesThenNull()
    {
        var bad = """{"title":"","contentMd":"","rationale":"","normalizedQuestion":""}""";
        var (distiller, claude) = NewDistiller(bad, bad, bad);

        var result = await distiller.MergeModulesAsync(Tenant, "A", "ca", "B", "cb");

        result.Should().BeNull();
        await claude.Received(3).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- ComputeDedupHash (thuần) ----

    [Fact]
    public void ComputeDedupHash_NormalizesCasePunctuationWhitespace()
    {
        var h1 = KnowledgeDistiller.ComputeDedupHash("Học phí HSK4?");
        var h2 = KnowledgeDistiller.ComputeDedupHash("  học phí   hsk4  ");

        h1.Should().Be(h2);
        h1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeDedupHash_DifferentQuestions_DifferentHash()
    {
        var h1 = KnowledgeDistiller.ComputeDedupHash("học phí hsk4");
        var h2 = KnowledgeDistiller.ComputeDedupHash("lịch khai giảng");

        h1.Should().NotBe(h2);
    }
}
