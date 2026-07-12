using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

// ai-self-learning-memory Lớp 3: bài học reviewer từ lý do reject + nén KB weekly (merge luôn chờ người).
public sealed class AgentMemoryAndKbCompressionJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task AgentMemoryDistillation_adds_lesson_from_reject_reasons()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "bài bịa giá", createdBy: null, Now.AddHours(-3));
        item.Reject(Now.AddHours(-2), "bịa giá khóa học 3tr");
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();

        var chat = new ScriptedChat("""
        {"ops":[{"op":"add","factId":null,"fact":"Content hay bịa giá khóa học","category":"mistake","confidence":0.9}]}
        """);
        var job = new AgentMemoryDistillationJob(
            fx.Db, new AgentMistakeExtractor(chat, new NoopLlmScope()), IdentityPii(), FixedClock(),
            NullLogger<AgentMemoryDistillationJob>.Instance);

        await job.RunAsync();

        var memory = await fx.Db.AgentMemories.IgnoreQueryFilters().SingleAsync();
        memory.AgentCode.Should().Be("reviewer-agent");
        memory.Fact.Should().Be("Content hay bịa giá khóa học");
    }

    [Fact]
    public async Task AgentMemoryDistillation_update_supersedes_old_lesson()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "bài", createdBy: null, Now.AddHours(-3));
        item.Reject(Now.AddHours(-2), "lại bịa giá");
        fx.Db.ContentItems.Add(item);
        var old = AgentMemory.Create(fx.TenantId, "reviewer-agent", "Hay bịa giá", "mistake", 0.7m, Now.AddDays(-5));
        fx.Db.AgentMemories.Add(old);
        await fx.Db.SaveChangesAsync();

        var chat = new ScriptedChat(
            $$"""{"ops":[{"op":"update","factId":"{{old.Id}}","fact":"Hay bịa giá và lịch khai giảng","category":"mistake","confidence":0.9}]}""");
        var job = new AgentMemoryDistillationJob(
            fx.Db, new AgentMistakeExtractor(chat, new NoopLlmScope()), IdentityPii(), FixedClock(),
            NullLogger<AgentMemoryDistillationJob>.Instance);

        await job.RunAsync();

        var all = await fx.Db.AgentMemories.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
        all.Single(m => m.Id == old.Id).IsActive.Should().BeFalse();
        all.Single(m => m.Id != old.Id).Fact.Should().Contain("lịch khai giảng");
    }

    [Fact]
    public async Task KbCompression_creates_pending_merge_suggestion_never_auto()
    {
        using var fx = new TestAppDb();
        var (targetId, sourceId) = await SeedTwoDeployedModulesAsync(fx);
        // Thứ tự call: propose merges -> merge full content -> reviewer verdict.
        var chat = new ScriptedChat(
            $$"""{"merges":[{"targetModuleId":"{{targetId}}","sourceModuleId":"{{sourceId}}","reason":"trùng chủ đề học phí"}]}""",
            """{"title":"Học phí (gộp)","contentMd":"## Học phí đầy đủ","rationale":"gộp 2 nhóm trùng","normalizedQuestion":"merge hoc-phi hoc-phi-hsk"}""",
            """{"verdict":"approve","reason":"gộp hợp lý"}""");
        var job = BuildCompressionJob(fx, chat, out var published);

        await job.RunForTenantAsync(fx.TenantId);

        var suggestion = await fx.Db.KbSuggestions.IgnoreQueryFilters().SingleAsync();
        suggestion.Op.Should().Be(KbSuggestion.OpMerge);
        suggestion.TargetKbModuleId.Should().Be(targetId);
        suggestion.Status.Should().Be(KbSuggestion.StatusPending); // verdict approve nhưng KHÔNG auto
        suggestion.AccuracyBefore.Should().BeNull();
        suggestion.IsAutoApprovable.Should().BeFalse();
        published.Should().ContainSingle(n => n.Type == "kb_suggestion_pending");
    }

    [Fact]
    public async Task KbCompression_second_run_dedups_by_pair()
    {
        using var fx = new TestAppDb();
        var (targetId, sourceId) = await SeedTwoDeployedModulesAsync(fx);
        var merges = $$"""{"merges":[{"targetModuleId":"{{targetId}}","sourceModuleId":"{{sourceId}}","reason":"trùng"}]}""";
        var chat = new ScriptedChat(
            merges,
            """{"title":"Gộp","contentMd":"## Gộp","rationale":"r","normalizedQuestion":"merge a b"}""",
            """{"verdict":"approve","reason":"ok"}""",
            merges); // lượt 2: cùng cặp -> dedup trước khi gọi merge/review

        var job = BuildCompressionJob(fx, chat, out _);
        await job.RunForTenantAsync(fx.TenantId);
        await job.RunForTenantAsync(fx.TenantId);

        (await fx.Db.KbSuggestions.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    private static async Task<(Guid TargetId, Guid SourceId)> SeedTwoDeployedModulesAsync(TestAppDb fx)
    {
        var target = KbModule.Create(fx.TenantId, "hoc-phi", "Học phí", Now.AddDays(-60));
        var source = KbModule.Create(fx.TenantId, "hoc-phi-hsk", "Học phí HSK", Now.AddDays(-50));
        fx.Db.KbModules.AddRange(target, source);
        var v1 = KbVersion.Create(target.Id, 1, "## Học phí chung 5tr", Now.AddDays(-60));
        v1.Deploy(Now.AddDays(-60));
        var v2 = KbVersion.Create(source.Id, 1, "## Học phí HSK 5tr", Now.AddDays(-50));
        v2.Deploy(Now.AddDays(-50));
        fx.Db.KbVersions.AddRange(v1, v2);
        await fx.Db.SaveChangesAsync();
        return (target.Id, source.Id);
    }

    private static KbCompressionJob BuildCompressionJob(TestAppDb fx, ScriptedChat chat, out List<NotificationRequest> published)
    {
        var captured = new List<NotificationRequest>();
        var publisher = Substitute.For<INotificationPublisher>();
        publisher.PublishAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => captured.Add(call.Arg<NotificationRequest>()));
        published = captured;
        var scope = new NoopLlmScope();
        return new KbCompressionJob(
            fx.Db,
            new KnowledgeDistiller(chat, scope),
            new ContentReviewer(chat, scope),
            IdentityPii(),
            publisher,
            FixedClock(),
            NullLogger<KbCompressionJob>.Instance);
    }

    private static IPiiRedactor IdentityPii()
    {
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new RedactionResult(call.Arg<string>(), []));
        return pii;
    }

    private static IClock FixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private sealed class ScriptedChat(params string[] responses) : IClaudeChatClient
    {
        private readonly Queue<string> _responses = new(responses);

        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            Task.FromResult(new ClaudeReply(_responses.Count > 0 ? _responses.Dequeue() : "{}", 1, 1, 0.01m, "test"));

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
}
