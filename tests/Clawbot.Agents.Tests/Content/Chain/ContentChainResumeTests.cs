using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content.Chain;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Tests.Content.Chain;

// P4 (§4.5): snapshot L1/L2 round-trip + resume chạy lại CHỈ L3+L4 với plan/outline đã lưu.
// Fake IClaudeChatClient trả JSON theo từng step nên test không cần LLM/mạng thật.
public sealed class ContentChainResumeTests
{
    // ===== ContentChainSnapshot — round-trip + khoan dung =====

    [Fact]
    public void Snapshot_RoundTrips_PlanAndOutline()
    {
        var plan = SamplePlan();
        var outline = SampleOutline();

        var planJson = ContentChainSnapshot.SerializePlan(plan);
        var outlineJson = ContentChainSnapshot.SerializeOutline(outline);
        var restored = ContentChainSnapshot.TryDeserialize(planJson, outlineJson);

        restored.Should().NotBeNull();
        restored!.Plan.KeyMessage.Should().Be(plan.KeyMessage);
        restored.Plan.Cta.Type.Should().Be(plan.Cta.Type);
        restored.Outline.Hooks.Should().Equal(outline.Hooks);
        restored.Outline.SelectedHookIndex.Should().Be(outline.SelectedHookIndex);
        restored.Outline.ProofPoints.Should().HaveCount(1);
        restored.Outline.ProofPoints[0].CitationId.Should().Be(1);
    }

    [Fact]
    public void Snapshot_SerializeReturnsNull_WhenInputNull()
    {
        ContentChainSnapshot.SerializePlan(null).Should().BeNull();
        ContentChainSnapshot.SerializeOutline(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("not json", "not json")]
    public void Snapshot_TryDeserializeReturnsNull_WhenAnyMissingOrBroken(string? planJson, string? outlineJson)
    {
        ContentChainSnapshot.TryDeserialize(planJson, outlineJson).Should().BeNull();
    }

    [Fact]
    public void Snapshot_TryDeserializeReturnsNull_WhenOutlineMissing()
    {
        var planJson = ContentChainSnapshot.SerializePlan(SamplePlan());

        // Thiếu outline => resume không chạy được => null (caller chạy full chuỗi từ body).
        ContentChainSnapshot.TryDeserialize(planJson, null).Should().BeNull();
    }

    [Fact]
    public void Snapshot_TryDeserializeReturnsNull_WhenPlanLacksKeyMessage()
    {
        // Plan hỏng cấu trúc (thiếu keyMessage) => coi như không dùng được.
        const string planJson = """{"objective":"awareness","cta":{"type":"inbox","text":"nhan tin"},"language":"vi"}""";
        var outlineJson = ContentChainSnapshot.SerializeOutline(SampleOutline());

        ContentChainSnapshot.TryDeserialize(planJson, outlineJson).Should().BeNull();
    }

    // ===== ResumeFromWriteAsync — chỉ chạy L3+L4 =====

    [Fact]
    public async Task Resume_RunsOnlyWriteAndPackage_WhenPlanOutlinePresent()
    {
        var fake = new FakeChatClient();
        var chain = BuildChain(fake);
        var context = ResumeContext();

        var outcome = await chain.ResumeFromWriteAsync(context);

        outcome.Succeeded.Should().BeTrue();
        // Đúng 2 mắt xích chạy: write (L3) + package (L4). L1/L2 bị bỏ.
        fake.CalledSteps.Should().Equal("write", "package");
        outcome.Traces.Should().HaveCount(2);
        outcome.Traces.Select(t => t.StepId).Should().Equal("write", "package");
        // Body cuối = caption + hashtags (merge L4).
        outcome.Body.Should().Contain("#hoctiengtrung");
        outcome.Plan.Should().NotBeNull();
        outcome.Outline.Should().NotBeNull();
    }

    [Fact]
    public async Task Resume_Throws_WhenPlanOrOutlineMissing()
    {
        var chain = BuildChain(new FakeChatClient());
        var context = ResumeContext() with { Plan = null };

        var act = () => chain.ResumeFromWriteAsync(context);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_RunsAllFourSteps()
    {
        var fake = new FakeChatClient();
        var chain = BuildChain(fake);
        var context = FullContext();

        var outcome = await chain.RunAsync(context);

        outcome.Succeeded.Should().BeTrue();
        fake.CalledSteps.Should().Equal("plan", "outline", "write", "package");
    }

    // ===== helpers =====

    private static ContentChain BuildChain(IClaudeChatClient claude)
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
        return new ContentChain(steps, claude, options);
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

    private static ContentChainContext FullContext() =>
        new(
            TenantId: Guid.NewGuid(),
            Platform: "facebook",
            Brief: "Viết bài quảng bá khóa tiếng Trung giao tiếp cho người đi làm",
            Knowledge: "[1] (module=kb, score=0.90) 90% học viên tiến bộ sau 3 tháng",
            PlatformTemplate: "Giọng Facebook thân thiện",
            Limits: new ContentChainLimits(Min: 10, Max: 5000),
            ChunkCount: 1);

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
            Hooks: ["Bạn muốn nói tiếng Trung tự tin sau 3 tháng?", "Học tiếng Trung giao tiếp cho người bận rộn", "Lịch khai giảng tháng này đã sẵn sàng"],
            SelectedHookIndex: 0,
            Sections: [new ContentOutlineSection("Mở bài", ["điểm một", "điểm hai"])],
            ProofPoints: [new ContentProofPoint("90% học viên tiến bộ sau 3 tháng", 1)],
            RiskFlags: ["Tránh cam kết tuyệt đối"],
            DroppedProofPoints: 0);

    // Fake trả JSON hợp lệ theo từng step, dựa vào dấu hiệu trong system prompt của step.
    private sealed class FakeChatClient : IClaudeChatClient
    {
        public List<string> CalledSteps { get; } = [];

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            var step = InferStep(systemPrompt);
            CalledSteps.Add(step);
            var text = step switch
            {
                "plan" => """{"objective":"awareness","audience":"nguoi di lam","keyMessage":"Khoa tieng Trung giao tiep","offer":null,"tone":"than thien","cta":{"type":"inbox","text":"Nhan tin de duoc tu van"},"mustInclude":["lich khai giang"],"mustAvoid":[],"language":"vi"}""",
                "outline" => """{"hooks":["Ban muon noi tieng Trung tu tin sau 3 thang khong nhi","Hoc tieng Trung giao tiep cho nguoi ban ron","Lich khai giang thang nay da san sang roi"],"outline":[{"section":"Mo bai","points":["diem mot","diem hai"]}],"proofPoints":[{"claim":"90 phan tram hoc vien tien bo","citationId":1}],"riskFlags":["Tranh cam ket tuyet doi"]}""",
                "package" => """{"caption":"Khoa tieng Trung giao tiep cho nguoi di lam ban ron, nhan tin de duoc tu van chi tiet nhe ban oi.","hashtags":["#hoctiengtrung","#hsk"],"firstComment":null,"altText":null}""",
                _ => "Khóa tiếng Trung giao tiếp cho người đi làm bận rộn. Nhắn tin để được tư vấn chi tiết và nhận lịch khai giảng nhé bạn ơi.",
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

        // Nhận diện step qua từ khóa persona trong system prompt (không phụ thuộc thứ tự gọi).
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
}
