using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using FluentAssertions;

namespace Clawbot.Api.Tests.Services;

public sealed class KbGeneratedCaseParsingTests
{
    [Fact]
    public void ParseGeneratedCases_ValidArray_IsParsed()
    {
        var cases = KbTestRunnerService.ParseGeneratedCases(
            """[{"question":"Học phí bao nhiêu?","expectedAnswer":"5 triệu/khoá"}]""");

        cases.Should().ContainSingle();
        cases[0].Question.Should().Be("Học phí bao nhiêu?");
        cases[0].ExpectedAnswer.Should().Be("5 triệu/khoá");
    }

    [Fact]
    public void ParseGeneratedCases_ExtractsArrayFromSurroundingProse()
    {
        var cases = KbTestRunnerService.ParseGeneratedCases(
            "Đây là kết quả:\n```json\n[{\"question\":\"Q\",\"expectedAnswer\":\"A\"}]\n```");

        cases.Should().ContainSingle();
    }

    [Fact]
    public void ParseGeneratedCases_TrimsWhitespace()
    {
        var cases = KbTestRunnerService.ParseGeneratedCases(
            """[{"question":"  Q  ","expectedAnswer":"  A  "}]""");

        cases[0].Question.Should().Be("Q");
        cases[0].ExpectedAnswer.Should().Be("A");
    }

    [Fact]
    public void ParseGeneratedCases_SkipsIncompleteItems()
    {
        var cases = KbTestRunnerService.ParseGeneratedCases(
            """
            [{"question":"Q1","expectedAnswer":"A1"},
             {"question":"Q2"},
             {"expectedAnswer":"A3"},
             {"question":"  ","expectedAnswer":"A4"},
             {"question":5,"expectedAnswer":"A5"},
             "khong-phai-object"]
            """);

        cases.Should().ContainSingle();
        cases[0].Question.Should().Be("Q1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ khong-phai-json")]
    public void ParseGeneratedCases_UnusableResponse_ReturnsEmpty(string? text)
    {
        KbTestRunnerService.ParseGeneratedCases(text!).Should().BeEmpty();
    }

    [Fact]
    public void ParseGeneratedCases_NonArrayJson_ReturnsEmpty()
    {
        KbTestRunnerService.ParseGeneratedCases("""{"question":"Q"}""").Should().BeEmpty();
    }
}

public sealed class KbClaudeEvaluationParsingTests
{
    [Fact]
    public void ParseClaudeEvaluation_Passed_IsParsedWithReason()
    {
        var result = KbTestRunnerService.ParseClaudeEvaluation(
            """{"passed":true,"reason":"Context có đủ thông tin"}""");

        result.Passed.Should().BeTrue();
        result.Reason.Should().Be("Context có đủ thông tin");
    }

    [Fact]
    public void ParseClaudeEvaluation_Failed_IsParsed()
    {
        var result = KbTestRunnerService.ParseClaudeEvaluation(
            """{"passed":false,"reason":"Thiếu dữ kiện"}""");

        result.Passed.Should().BeFalse();
        result.Reason.Should().Be("Thiếu dữ kiện");
    }

    [Fact]
    public void ParseClaudeEvaluation_MissingPassed_DefaultsToFailed()
    {
        // Không khẳng định pass thì phải coi là trượt — không được mặc định "đạt".
        KbTestRunnerService.ParseClaudeEvaluation("""{"reason":"khong ro"}""")
            .Passed.Should().BeFalse();
    }

    [Fact]
    public void ParseClaudeEvaluation_NonBooleanPassed_IsFailed()
    {
        KbTestRunnerService.ParseClaudeEvaluation("""{"passed":"true"}""")
            .Passed.Should().BeFalse();
    }

    [Fact]
    public void ParseClaudeEvaluation_BlankReason_BecomesNull()
    {
        KbTestRunnerService.ParseClaudeEvaluation("""{"passed":true,"reason":"   "}""")
            .Reason.Should().BeNull();
    }

    [Fact]
    public void ParseClaudeEvaluation_ExtractsObjectFromProse()
    {
        var result = KbTestRunnerService.ParseClaudeEvaluation(
            "Kết luận: {\"passed\":true,\"reason\":\"ok\"} — hết.");

        result.Passed.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseClaudeEvaluation_EmptyResponse_IsFlaggedDistinctly(string? text)
    {
        var result = KbTestRunnerService.ParseClaudeEvaluation(text!);

        result.Passed.Should().BeFalse();
        result.Reason.Should().Be("empty_evaluator_response");
    }

    [Fact]
    public void ParseClaudeEvaluation_MalformedJson_IsFlaggedDistinctly()
    {
        var result = KbTestRunnerService.ParseClaudeEvaluation("{ hong");

        result.Passed.Should().BeFalse();
        result.Reason.Should().Be("invalid_evaluator_response");
    }
}

public sealed class KbTestRunnerEvaluateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static KbTestCase Case() =>
        KbTestCase.Create(Guid.NewGuid(), "Học phí bao nhiêu?", "5 triệu", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task EvaluateAsync_NoChunksRetrieved_FailsWithNoContextReason()
    {
        // Phân biệt "hạ tầng thiếu dữ liệu" với "AI trả lời sai" — kho vector rỗng phải ra mã riêng
        // và KHÔNG được gọi LLM (tốn tiền mà che mất nguyên nhân thật).
        var claude = new StubClaude("""{"passed":true}""");
        var runner = new KbTestRunnerService(new StubRag([]), claude, new StubScope());

        var result = await runner.EvaluateAsync(TenantId, "hoc-phi", Case(), CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.Answer.Should().Be(KbTestRunnerService.NoContextReason);
        claude.Calls.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateAsync_ChunksRetrieved_UsesEvaluatorVerdict()
    {
        var runner = new KbTestRunnerService(
            new StubRag([new RagChunk("v1", "hoc-phi", "Học phí 5 triệu/khoá", 0.9f)]),
            new StubClaude("""{"passed":true,"reason":"khớp"}"""),
            new StubScope());

        var testCase = Case();
        var result = await runner.EvaluateAsync(TenantId, "hoc-phi", testCase, CancellationToken.None);

        result.Passed.Should().BeTrue();
        result.Answer.Should().Be("khớp");
        result.TestCaseId.Should().Be(testCase.Id);
        result.Question.Should().Be(testCase.Question);
    }

    [Fact]
    public async Task EvaluateAsync_PassesQuestionAndContextToEvaluator()
    {
        var claude = new StubClaude("""{"passed":false,"reason":"thiếu"}""");
        var runner = new KbTestRunnerService(
            new StubRag([new RagChunk("v1", "hoc-phi", "Đoạn ngữ cảnh A", 0.8f)]),
            claude,
            new StubScope());

        await runner.EvaluateAsync(TenantId, "hoc-phi", Case(), CancellationToken.None);

        claude.LastUserMessage.Should().Contain("Đoạn ngữ cảnh A");
        claude.LastUserMessage.Should().Contain("Học phí bao nhiêu?");
        claude.LastUserMessage.Should().Contain("5 triệu");
    }

    [Fact]
    public async Task EvaluateAsync_RequestsTopThreeChunksForModule()
    {
        var rag = new StubRag([new RagChunk("v1", "hoc-phi", "x", 1f)]);
        var runner = new KbTestRunnerService(rag, new StubClaude("{}"), new StubScope());

        await runner.EvaluateAsync(TenantId, "hoc-phi", Case(), CancellationToken.None);

        rag.LastRequest!.TenantId.Should().Be(TenantId);
        rag.LastRequest.KbModuleCode.Should().Be("hoc-phi");
        rag.LastRequest.TopK.Should().Be(3);
    }
}

public sealed class KbTestRunnerGenerateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static KbTestRunnerService Runner(StubClaude claude) =>
        new(new StubRag([]), claude, new StubScope());

    [Theory]
    [InlineData(null, 5)]
    [InlineData("", 5)]
    [InlineData("   ", 5)]
    [InlineData("nội dung", 0)]
    [InlineData("nội dung", -1)]
    public async Task GenerateCasesAsync_NoWorkToDo_ReturnsEmptyWithoutCallingLlm(string? content, int count)
    {
        var claude = new StubClaude("[]");

        var cases = await Runner(claude).GenerateCasesAsync(
            TenantId, content!, count, CancellationToken.None);

        cases.Should().BeEmpty();
        claude.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GenerateCasesAsync_SingleBatchCoversRequestedCount()
    {
        var claude = new StubClaude(
            """[{"question":"Q1","expectedAnswer":"A1"},{"question":"Q2","expectedAnswer":"A2"}]""");

        var cases = await Runner(claude).GenerateCasesAsync(
            TenantId, "nội dung KB", 2, CancellationToken.None);

        cases.Should().HaveCount(2);
        claude.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GenerateCasesAsync_NeverExceedsRequestedCount()
    {
        var claude = new StubClaude(
            """[{"question":"Q1","expectedAnswer":"A1"},{"question":"Q2","expectedAnswer":"A2"},{"question":"Q3","expectedAnswer":"A3"}]""");

        var cases = await Runner(claude).GenerateCasesAsync(
            TenantId, "nội dung", 2, CancellationToken.None);

        cases.Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerateCasesAsync_DeduplicatesQuestionsAcrossBatches()
    {
        // Lô sau trả lại đúng câu cũ: phải bỏ trùng và dừng sau 2 lô trống liên tiếp.
        var claude = new StubClaude("""[{"question":"Q1","expectedAnswer":"A1"}]""");

        var cases = await Runner(claude).GenerateCasesAsync(
            TenantId, "nội dung", 5, CancellationToken.None);

        cases.Should().ContainSingle();
        claude.Calls.Should().Be(3);
    }

    [Fact]
    public async Task GenerateCasesAsync_DeduplicationIsCaseInsensitive()
    {
        var claude = new StubClaude(
            """[{"question":"Học phí?","expectedAnswer":"A"},{"question":"HỌC PHÍ?","expectedAnswer":"B"}]""");

        var cases = await Runner(claude).GenerateCasesAsync(
            TenantId, "nội dung", 5, CancellationToken.None);

        cases.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateCasesAsync_LlmFailure_KeepsWhatWasAlreadyGenerated()
    {
        // Một lô hỏng (lỗi stream) không được xoá sạch thành quả các lô trước.
        var claude = new StubClaude("""[{"question":"Q1","expectedAnswer":"A1"}]""")
        {
            ThrowOnCall = 2,
        };

        var cases = await Runner(claude).GenerateCasesAsync(
            TenantId, "nội dung", 20, CancellationToken.None);

        cases.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateCasesAsync_FirstCallFails_ReturnsEmptyInsteadOfThrowing()
    {
        var claude = new StubClaude("[]") { ThrowOnCall = 1 };

        var cases = await Runner(claude).GenerateCasesAsync(
            TenantId, "nội dung", 5, CancellationToken.None);

        cases.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateCasesAsync_PromptListsAlreadyGeneratedQuestions()
    {
        var claude = new StubClaude("""[{"question":"Q1","expectedAnswer":"A1"}]""");

        await Runner(claude).GenerateCasesAsync(TenantId, "nội dung", 5, CancellationToken.None);

        claude.LastUserMessage.Should().Contain("KHÔNG lặp lại");
        claude.LastUserMessage.Should().Contain("- Q1");
    }

    [Fact]
    public async Task GenerateCasesAsync_TruncatesOversizedContent()
    {
        var claude = new StubClaude("""[{"question":"Q","expectedAnswer":"A"}]""");
        var huge = new string('x', 30_000);

        await Runner(claude).GenerateCasesAsync(TenantId, huge, 1, CancellationToken.None);

        // Trần 24k ký tự nội dung; prompt còn phần khung nên chỉ kiểm phần nội dung bị cắt.
        claude.LastUserMessage!.Split("Knowledge base content:\n")[1]
            .Split("\n\nGenerate")[0].Length.Should().Be(24_000);
    }
}

internal sealed class StubRag(IReadOnlyList<RagChunk> chunks) : IRagRetriever
{
    public RagRequest? LastRequest { get; private set; }

    public Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(chunks);
    }
}

internal sealed class StubClaude(string replyText) : IClaudeChatClient
{
    public int Calls { get; private set; }

    public string? LastUserMessage { get; private set; }

    /// <summary>Số thứ tự lượt gọi sẽ ném lỗi (1 = lượt đầu). 0 = không bao giờ ném.</summary>
    public int ThrowOnCall { get; init; }

    public Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default)
    {
        Calls++;
        LastUserMessage = userMessage;
        if (ThrowOnCall == Calls)
            throw new InvalidOperationException("stream reported failure");

        return Task.FromResult(new ClaudeReply(replyText, 10, 20, 0.001m));
    }

    public IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default) =>
        throw new NotSupportedException();
}

internal sealed class StubScope : ILlmCallScope
{
    public LlmCallContext? Current => null;

    public IDisposable Begin(
        Guid tenantId,
        string agentCode,
        DateTimeOffset? costAt = null,
        Guid? reservationId = null,
        Guid? sessionId = null) => new NoopDisposable();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
