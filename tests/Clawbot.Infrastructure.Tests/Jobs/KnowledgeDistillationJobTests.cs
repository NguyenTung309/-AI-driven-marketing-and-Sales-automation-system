using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Learning;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

// ai-self-learning-memory Lớp 1: job chưng cất chạy với LLM script sẵn (thứ tự call cố định:
// distill -> consolidate -> reviewer -> judge before -> judge after). Rail auto-approve test đủ 4 nhánh.
public sealed class KnowledgeDistillationJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 19, 0, 0, TimeSpan.Zero);

    private const string DraftJson =
        """{"title":"Học phí HSK4","contentMd":"## Học phí\n5 triệu/khóa 3 tháng","rationale":"AI trượt","normalizedQuestion":"học phí hsk4"}""";

    private const string ReviewApprove = """{"verdict":"approve","reason":"khớp bằng chứng"}""";
    private const string ReviewNeedsHuman = """{"verdict":"needs_human","reason":"không chắc"}""";
    private const string JudgePass = """{"passed":true}""";
    private const string JudgeFail = """{"passed":false}""";

    private static string ConsolidateUpdate(Guid moduleId) =>
        $$"""{"op":"update","targetModuleId":"{{moduleId}}","mergedContentMd":"## Học phí\n5 triệu/khóa"}""";

    [Fact]
    public async Task Rail_met_auto_mode_deploys_and_notifies_auto_approved()
    {
        using var harness = new Harness();
        var moduleId = await harness.SeedEscalatedConversationWithModuleAsync(withTestCase: true);
        harness.Script(DraftJson, ConsolidateUpdate(moduleId), ReviewApprove, JudgeFail, JudgePass);

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        var suggestion = await harness.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.Status.Should().Be(KbSuggestion.StatusApproved);
        suggestion.ApprovalMode.Should().Be(KbSuggestion.ApprovalModeAuto);
        suggestion.AccuracyBefore.Should().Be(0m);
        suggestion.AccuracyAfter.Should().Be(100m);
        harness.Materializer.Materialized.Should().ContainSingle().Which.Id.Should().Be(suggestion.Id);
        harness.Published.Should().Contain(n => n.Type == "kb_suggestion_auto_approved");
    }

    [Fact]
    public async Task Verdict_not_approve_stays_pending()
    {
        using var harness = new Harness();
        var moduleId = await harness.SeedEscalatedConversationWithModuleAsync(withTestCase: true);
        harness.Script(DraftJson, ConsolidateUpdate(moduleId), ReviewNeedsHuman, JudgeFail, JudgePass);

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        var suggestion = await harness.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.Status.Should().Be(KbSuggestion.StatusPending);
        harness.Materializer.Materialized.Should().BeEmpty();
        harness.Published.Should().Contain(n => n.Type == "kb_suggestion_pending");
    }

    [Fact]
    public async Task Accuracy_declines_stays_pending()
    {
        using var harness = new Harness();
        var moduleId = await harness.SeedEscalatedConversationWithModuleAsync(withTestCase: true);
        harness.Script(DraftJson, ConsolidateUpdate(moduleId), ReviewApprove, JudgePass, JudgeFail);

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        var suggestion = await harness.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.Status.Should().Be(KbSuggestion.StatusPending);
        suggestion.AccuracyBefore.Should().Be(100m);
        suggestion.AccuracyAfter.Should().Be(0m);
        harness.Materializer.Materialized.Should().BeEmpty();
    }

    [Fact]
    public async Task Op_add_has_no_accuracy_and_stays_pending_even_with_approve()
    {
        // Tri thức mới hoàn toàn: không test case => accuracy NULL => không bao giờ auto "mù".
        using var harness = new Harness();
        await harness.SeedEscalatedConversationWithModuleAsync(withTestCase: false, moduleActive: false);
        harness.Script(DraftJson, """{"op":"add","targetModuleId":null,"mergedContentMd":null}""", ReviewApprove);

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        var suggestion = await harness.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.Op.Should().Be(KbSuggestion.OpAdd);
        suggestion.AccuracyBefore.Should().BeNull();
        suggestion.Status.Should().Be(KbSuggestion.StatusPending);
        harness.Materializer.Materialized.Should().BeEmpty();
    }

    [Fact]
    public async Task Tenant_flag_forces_pending_even_when_rail_met()
    {
        using var harness = new Harness();
        var moduleId = await harness.SeedEscalatedConversationWithModuleAsync(withTestCase: true);
        harness.Script(DraftJson, ConsolidateUpdate(moduleId), ReviewApprove, JudgeFail, JudgePass);

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: true);

        var suggestion = await harness.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.Status.Should().Be(KbSuggestion.StatusPending);
        // Rail vẫn đạt (IsAutoApprovable true) — flag tenant chặn ở tầng job, người duyệt sau vẫn thấy đủ tín hiệu.
        suggestion.IsAutoApprovable.Should().BeTrue();
        harness.Materializer.Materialized.Should().BeEmpty();
    }

    [Fact]
    public async Task Second_run_dedups_by_hash()
    {
        using var harness = new Harness();
        var moduleId = await harness.SeedEscalatedConversationWithModuleAsync(withTestCase: true);
        harness.Script(
            DraftJson, ConsolidateUpdate(moduleId), ReviewNeedsHuman, JudgeFail, JudgePass,
            DraftJson); // lượt 2: distill trả cùng normalizedQuestion -> hash trùng -> skip trước consolidate

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);
        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        (await harness.Db.KbSuggestions.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Mines_sale_answered_source_as_add_suggestion()
    {
        using var harness = new Harness();
        await harness.SeedSaleAnsweredConversationAsync();
        // op=add (chưa có module) => distill -> consolidate add -> reviewer; accuracy NULL nên chờ người.
        harness.Script(
            """{"title":"Lớp mất gốc","contentMd":"## Sơ cấp\nKhai giảng đầu tháng","rationale":"sale trả lời tay","normalizedQuestion":"lop mat goc"}""",
            """{"op":"add","targetModuleId":null,"mergedContentMd":null}""",
            ReviewApprove);

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        var suggestion = await harness.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.Op.Should().Be(KbSuggestion.OpAdd);
        suggestion.Status.Should().Be(KbSuggestion.StatusPending);
        suggestion.EvidenceJson.Should().Contain("sale_answered");
    }

    [Fact]
    public async Task Mines_repeated_question_source()
    {
        using var harness = new Harness();
        await harness.SeedRepeatedQuestionAsync(times: 3);
        harness.Script(
            """{"title":"Học online","contentMd":"## Online\nCó lớp online","rationale":"hỏi lặp nhiều","normalizedQuestion":"day online"}""",
            """{"op":"add","targetModuleId":null,"mergedContentMd":null}""",
            ReviewApprove);

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        var suggestion = await harness.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.EvidenceJson.Should().Contain("repeated_question");
    }

    [Fact]
    public async Task Repeated_question_below_threshold_not_mined()
    {
        using var harness = new Harness();
        await harness.SeedRepeatedQuestionAsync(times: 2); // < ngưỡng 3

        await harness.Job.RunForTenantAsync(harness.TenantId, requireHumanReview: false);

        (await harness.Db.KbSuggestions.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TestAppDb _testDb;
        private readonly ScriptedChatClient _chat = new();

        public Harness()
        {
            _testDb = new TestAppDb();
            var scope = new NoopLlmScope();
            var pii = Substitute.For<IPiiRedactor>();
            pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => new RedactionResult(call.Arg<string>(), []));
            var rag = Substitute.For<IRagRetriever>();
            rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
                .Returns([]);
            var publisher = Substitute.For<INotificationPublisher>();
            publisher.PublishAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask)
                .AndDoes(call => Published.Add(call.Arg<NotificationRequest>()));
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);

            Materializer = new RecordingMaterializer(Db, clock);
            Job = new KnowledgeDistillationJob(
                Db,
                new KnowledgeDistiller(_chat, scope),
                new ContentReviewer(_chat, scope),
                new KbSuggestionAccuracyEvaluator(rag, _chat, scope),
                Materializer,
                pii,
                publisher,
                clock,
                Options.Create(new LearningOptions()),
                NullLogger<KnowledgeDistillationJob>.Instance);
        }

        public AppDbContext Db => _testDb.Db;
        public Guid TenantId => _testDb.TenantId;
        public KnowledgeDistillationJob Job { get; }
        public RecordingMaterializer Materializer { get; }
        public List<NotificationRequest> Published { get; } = [];

        public void Script(params string[] responses) => _chat.Enqueue(responses);

        // 1 hội thoại escalated (khách hỏi, bot trả lời kém, sale trả lời chuẩn) + 1 module deployed (+ test case).
        public async Task<Guid> SeedEscalatedConversationWithModuleAsync(bool withTestCase, bool moduleActive = true)
        {
            var conv = Clawbot.Domain.Conversations.Conversation.Open(TenantId, "zalo", "thread-1", Now.AddHours(-3));
            conv.AppendMessage("in", "contact", "Học phí HSK4 bao nhiêu?", "text", Now.AddHours(-2));
            conv.AppendMessage("out", "bot", "Xin lỗi, tôi chưa có thông tin.", "text", Now.AddHours(-2).AddMinutes(1));
            conv.AppendMessage("out", "user", "5 triệu/khóa 3 tháng em nhé.", "text", Now.AddHours(-1));
            conv.Escalate();
            Db.Conversations.Add(conv);

            var module = KbModule.Create(TenantId, "hoc-phi", "Học phí", Now.AddDays(-30));
            if (moduleActive)
            {
                Db.KbModules.Add(module);
                var version = KbVersion.Create(module.Id, 1, "## Học phí cũ", Now.AddDays(-30));
                version.Deploy(Now.AddDays(-30));
                Db.KbVersions.Add(version);
                if (withTestCase)
                    Db.KbTestCases.Add(KbTestCase.Create(module.Id, "Học phí HSK4 bao nhiêu?", "5 triệu/khóa", Now.AddDays(-10)));
            }

            await Db.SaveChangesAsync();
            return module.Id;
        }

        // Nguồn 2: hội thoại KHÔNG escalated, khách hỏi rồi sale (out/user) trả lời tay trong cửa sổ.
        public async Task SeedSaleAnsweredConversationAsync()
        {
            var conv = Clawbot.Domain.Conversations.Conversation.Open(TenantId, "zalo", "thread-sale", Now.AddHours(-3));
            conv.AppendMessage("in", "contact", "Có lớp cho người mất gốc không?", "text", Now.AddHours(-2));
            conv.AppendMessage("out", "user", "Có ạ, lớp sơ cấp khai giảng đầu tháng.", "text", Now.AddHours(-1));
            Db.Conversations.Add(conv);
            await Db.SaveChangesAsync();
        }

        // Nguồn 3: cùng 1 câu hỏi lặp ở >= 3 hội thoại khác nhau trong cửa sổ 7 ngày.
        public async Task SeedRepeatedQuestionAsync(int times = 3)
        {
            for (var i = 0; i < times; i++)
            {
                var conv = Clawbot.Domain.Conversations.Conversation.Open(TenantId, "zalo", $"thread-rep-{i}", Now.AddDays(-2));
                conv.AppendMessage("in", "contact", "Trung tâm có dạy online không?", "text", Now.AddDays(-2).AddMinutes(i));
                Db.Conversations.Add(conv);
            }
            await Db.SaveChangesAsync();
        }

        public void Dispose() => _testDb.Dispose();
    }

    // Trả lần lượt theo hàng đợi — thứ tự call của job là cố định nên script tuyến tính đủ dùng.
    private sealed class ScriptedChatClient : IClaudeChatClient
    {
        private readonly Queue<string> _responses = new();

        public void Enqueue(params string[] responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            Task.FromResult(new ClaudeReply(
                _responses.Count > 0 ? _responses.Dequeue() : "{}",
                1, 1, 0.01m, "test"));

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, 1, 1, 0.01m, "test");
        }
    }

    private sealed class NoopLlmScope : ILlmCallScope
    {
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
        public LlmCallContext? Current => null;
        public IDisposable Begin(Guid tenantId, string agentCode, DateTimeOffset? costAt = null, Guid? reservationId = null, Guid? sessionId = null) =>
            new NoopDisposable();
    }

    private sealed class RecordingMaterializer(AppDbContext db, IClock clock)
        : KbSuggestionMaterializer(db, null!, clock)
    {
        public List<KbSuggestion> Materialized { get; } = [];

        public override Task<KbVersion> MaterializeAsync(KbSuggestion suggestion, CancellationToken ct = default)
        {
            Materialized.Add(suggestion);
            return Task.FromResult(KbVersion.Create(Guid.NewGuid(), 1, suggestion.ContentMd, Now));
        }
    }
}
