using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Content.Chain;
using Clawbot.Domain.Content;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Tests.Content.Chain;

// P6 (§4.7): vòng refine bám reviewer — WriteStep bơm lý do reject vào L3, resume L3+L4 sửa bài,
// bộ đếm 1-vòng trên ContentReviewTask. Fake IClaudeChatClient nên test không cần LLM/mạng thật.
public sealed class ContentChainRefineTests
{
    // ===== WriteStep bơm RefineFeedback vào L3 (thuần, không LLM) =====

    [Fact]
    public void WriteStep_InjectsRefineFeedback_IntoUserPrompt()
    {
        var step = new WriteStep(Options.Create(new ContentChainOptions()));
        var context = ResumeContext() with { RefineFeedback = "Bỏ cam kết tuyệt đối, nêu số liệu có dẫn nguồn." };

        var prompt = step.BuildPrompt(context);

        prompt.User.Should().Contain("GÓP Ý CẦN KHẮC PHỤC");
        prompt.User.Should().Contain("Bỏ cam kết tuyệt đối");
    }

    [Fact]
    public void WriteStep_OmitsRefineSection_WhenNoFeedback()
    {
        var step = new WriteStep(Options.Create(new ContentChainOptions()));

        var prompt = step.BuildPrompt(ResumeContext());

        prompt.User.Should().NotContain("GÓP Ý CẦN KHẮC PHỤC");
    }

    // ===== ContentReviewTask.RecordRefineAttempt — đếm 1 vòng =====

    [Fact]
    public void RecordRefineAttempt_AllowsExactlyOneRound()
    {
        var task = LeasedTask(out var leaseToken, out var at);

        task.RecordRefineAttempt(leaseToken, at);

        task.RefineAttemptCount.Should().Be(1);
    }

    [Fact]
    public void RecordRefineAttempt_Throws_OnSecondRound()
    {
        var task = LeasedTask(out var leaseToken, out var at);
        task.RecordRefineAttempt(leaseToken, at);

        var act = () => task.RecordRefineAttempt(leaseToken, at);

        act.Should().Throw<InvalidOperationException>().WithMessage("content_review_task_refine_exhausted");
    }

    [Fact]
    public void RecordRefineAttempt_Throws_WhenLeaseMismatch()
    {
        var task = LeasedTask(out _, out var at);

        var act = () => task.RecordRefineAttempt(Guid.NewGuid(), at);

        act.Should().Throw<InvalidOperationException>();
    }

    // MEDIUM-1: lease hết hạn nhưng CHƯA worker nào reclaim (token trong row vẫn khớp) => RecordRefineAttempt ném
    // content_review_task_lease_expired. Đây đúng là InvalidOperationException mà Coordinator.TryApplyRefineAsync
    // phải bắt và degrade về null (không ném ra worker). Test khoá hợp đồng domain làm điểm tựa cho nhánh catch đó.
    [Fact]
    public void RecordRefineAttempt_Throws_WhenLeaseExpired()
    {
        var task = LeasedTask(out var leaseToken, out var at);

        var act = () => task.RecordRefineAttempt(leaseToken, at.AddMinutes(6));

        act.Should().Throw<InvalidOperationException>().WithMessage("content_review_task_lease_expired");
    }

    // ===== ContentItem.ApplyAgentRefine — đổi body giữ revision, review vẫn running =====

    [Fact]
    public void ApplyAgentRefine_ChangesBody_KeepsRevision_WhileReviewRunning()
    {
        var now = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(Guid.NewGuid(), "facebook", "Bản nháp cam kết 100% đỗ.", createdBy: null, now);
        var revisionBefore = item.ContentRevision;
        item.BeginAgentReview(item.ContentRevision, now);

        item.ApplyAgentRefine("Bản đã sửa, nêu 90% học viên tiến bộ (có dẫn nguồn).", now);

        item.Body.Should().StartWith("Bản đã sửa");
        item.ContentRevision.Should().Be(revisionBefore);
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
    }

    [Fact]
    public void ApplyAgentRefine_Throws_WhenReviewNotRunning()
    {
        var now = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(Guid.NewGuid(), "facebook", "Bản nháp.", createdBy: null, now);

        var act = () => item.ApplyAgentRefine("Bản sửa.", now);

        act.Should().Throw<InvalidOperationException>().WithMessage("content_review_not_running");
    }

    // ===== ContentAgent.RefineFromChainAsync — resume L3+L4 với lý do reject =====

    [Fact]
    public async Task Refine_RunsWriteAndPackage_WithRejectionFeedback()
    {
        var fake = new FakeChatClient();
        var agent = BuildAgent(fake);
        var planJson = ContentChainSnapshot.SerializePlan(SamplePlan());
        var outlineJson = ContentChainSnapshot.SerializeOutline(SampleOutline());

        var draft = await agent.RefineFromChainAsync(new ContentRefineFromChainRequest(
            Guid.NewGuid(), BriefId: null, "facebook", planJson, outlineJson,
            RejectionReason: "Bỏ cam kết tuyệt đối, nêu số liệu có dẫn nguồn."));

        draft.Should().NotBeNull();
        fake.CalledSteps.Should().Equal("write", "package");
        // Lý do reject phải tới được lần gọi L3 (write) — bằng chứng feedback được bơm vào prompt.
        fake.WriteUserPrompt.Should().Contain("Bỏ cam kết tuyệt đối");
        draft!.Body.Should().Contain("#hoctiengtrung");
    }

    [Fact]
    public async Task Refine_ReturnsNull_WhenSnapshotBroken()
    {
        var agent = BuildAgent(new FakeChatClient());

        var draft = await agent.RefineFromChainAsync(new ContentRefineFromChainRequest(
            Guid.NewGuid(), BriefId: null, "facebook", PlanJson: "not json", OutlineJson: "not json",
            RejectionReason: "lý do"));

        draft.Should().BeNull();
    }

    [Fact]
    public async Task Refine_Throws_WhenRejectionReasonBlank()
    {
        var agent = BuildAgent(new FakeChatClient());
        var planJson = ContentChainSnapshot.SerializePlan(SamplePlan());
        var outlineJson = ContentChainSnapshot.SerializeOutline(SampleOutline());

        var act = () => agent.RefineFromChainAsync(new ContentRefineFromChainRequest(
            Guid.NewGuid(), BriefId: null, "facebook", planJson, outlineJson, RejectionReason: "  "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ===== helpers =====

    private static Core.Content.ContentAgent BuildAgent(IClaudeChatClient claude)
    {
        var options = Options.Create(new ContentChainOptions
        {
            Enabled = true,
            StepTimeoutSeconds = 5,
            ChainTimeoutSeconds = 30,
        });
        IReadOnlyList<IContentChainStep> steps =
        [
            new PlanStep(options),
            new OutlineStep(options),
            new WriteStep(options),
            new PackageStep(options),
        ];
        var chain = new ContentChain(steps, claude, options);
        return new Core.Content.ContentAgent(
            new StubRag(),
            new StubTemplates(),
            claude,
            new StubLlmScope(),
            chain: chain,
            chainOptions: options);
    }

    private static ContentChainContext ResumeContext() =>
        new(
            TenantId: Guid.NewGuid(),
            Platform: "facebook",
            Brief: string.Empty,
            Knowledge: string.Empty,
            PlatformTemplate: "Giọng Facebook thân thiện",
            Limits: new ContentChainLimits(Min: 10, Max: 5000),
            ChunkCount: 0,
            Plan: SamplePlan(),
            Outline: SampleOutline());

    private static ContentReviewTask LeasedTask(out Guid leaseToken, out DateTimeOffset at)
    {
        at = DateTimeOffset.UtcNow;
        leaseToken = Guid.NewGuid();
        var task = ContentReviewTask.CreatePending(Guid.NewGuid(), Guid.NewGuid(), 1, at, at);
        task.Lease(leaseToken, at.AddMinutes(5), at);
        return task;
    }

    private static ContentPlan SamplePlan() =>
        new(
            Objective: "awareness",
            Audience: "người đi làm",
            KeyMessage: "Khóa tiếng Trung giao tiếp cho người bận rộn",
            Offer: null,
            Tone: "thân thiện",
            Cta: new ContentPlanCta("inbox", "Nhắn tin để được tư vấn"),
            MustInclude: ["lịch khai giảng"],
            MustAvoid: [],
            Language: "vi");

    private static ContentOutline SampleOutline() =>
        new(
            Hooks: ["Bạn muốn nói tiếng Trung tự tin sau 3 tháng?", "Học tiếng Trung giao tiếp cho người bận rộn"],
            SelectedHookIndex: 0,
            Sections: [new ContentOutlineSection("Mở bài", ["điểm một", "điểm hai"])],
            ProofPoints: [new ContentProofPoint("90% học viên tiến bộ sau 3 tháng", 1)],
            RiskFlags: [],
            DroppedProofPoints: 0);

    // Fake trả JSON hợp lệ theo từng step; ghi lại user prompt của write để kiểm feedback được bơm.
    private sealed class FakeChatClient : IClaudeChatClient
    {
        public List<string> CalledSteps { get; } = [];
        public string WriteUserPrompt { get; private set; } = string.Empty;

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            var step = InferStep(systemPrompt);
            CalledSteps.Add(step);
            if (step == "write")
                WriteUserPrompt = userMessage;
            var text = step switch
            {
                "package" => """{"caption":"Khoa tieng Trung giao tiep cho nguoi di lam ban ron, nhan tin de duoc tu van nhe.","hashtags":["#hoctiengtrung","#hsk"],"firstComment":null,"altText":null}""",
                _ => "Khóa tiếng Trung giao tiếp cho người đi làm bận rộn. Nhắn tin để được tư vấn và nhận lịch khai giảng nhé bạn ơi.",
            };
            return Task.FromResult(new ClaudeReply(text, 100, 50, 0.001m, "fake-model"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct).ConfigureAwait(false);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, reply.InputTokens, reply.OutputTokens, reply.UsdCost, reply.Model);
        }

        private static string InferStep(string system)
        {
            if (system.Contains("biên tập viên", StringComparison.Ordinal))
                return "outline";
            if (system.Contains("tối ưu bài đăng", StringComparison.Ordinal))
                return "package";
            if (system.Contains("người viết nội dung", StringComparison.Ordinal))
                return "write";
            return "plan";
        }
    }

    // Refine chỉ chạy L3+L4 với Plan+Outline lưu sẵn nên RAG/templates không được gọi — stub tối thiểu.
    private sealed class StubRag : Core.Rag.IRagRetriever
    {
        public Task<IReadOnlyList<Core.Rag.RagChunk>> RetrieveAsync(
            Core.Rag.RagRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Core.Rag.RagChunk>>(Array.Empty<Core.Rag.RagChunk>());
    }

    private sealed class StubTemplates : Core.Content.IPromptTemplateProvider
    {
        public string GetTemplate(string platform) => "Giọng nền tảng thân thiện.";
    }

    private sealed class StubLlmScope : ILlmCallScope
    {
        public LlmCallContext? Current => null;

        public IDisposable Begin(
            Guid tenantId,
            string agentCode,
            DateTimeOffset? costAt = null,
            Guid? reservationId = null,
            Guid? sessionId = null) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
