using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Learning;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class ContactMemoryExtractionJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scan_extracts_add_op_marks_watermark()
    {
        using var harness = new Harness("""
        {"ops":[{"op":"add","factId":null,"fact":"Học viên trình độ HSK3","category":"profile","confidence":0.9}]}
        """);
        var contactId = await harness.SeedConversationAsync(lastMessageAt: Now.AddMinutes(-30));

        await harness.Job.RunScanAsync();

        var memory = await harness.Db.ContactMemories.IgnoreQueryFilters().SingleAsync();
        memory.ContactId.Should().Be(contactId);
        memory.Fact.Should().Be("Học viên trình độ HSK3");
        var conv = await harness.Db.Conversations.IgnoreQueryFilters().SingleAsync();
        conv.MemoryExtractedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Scan_update_op_supersedes_old_fact()
    {
        using var harness = new Harness(""); // script set sau khi biết factId
        var contactId = await harness.SeedConversationAsync(lastMessageAt: Now.AddMinutes(-30));
        var old = ContactMemory.Create(harness.TenantId, contactId, "Thích ca tối 2-4-6", ContactMemory.CategoryPreference, 0.8m, null, Now.AddDays(-3));
        harness.Db.ContactMemories.Add(old);
        await harness.Db.SaveChangesAsync();
        harness.Chat.Script(
            $$"""{"ops":[{"op":"update","factId":"{{old.Id}}","fact":"Đổi sang ca tối 3-5-7","category":"preference","confidence":0.9}]}""");

        await harness.Job.RunScanAsync();

        var all = await harness.Db.ContactMemories.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
        var oldRow = all.Single(m => m.Id == old.Id);
        var newRow = all.Single(m => m.Id != old.Id);
        oldRow.IsActive.Should().BeFalse();
        oldRow.SupersededById.Should().Be(newRow.Id);
        newRow.Fact.Should().Be("Đổi sang ca tối 3-5-7");
        newRow.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_skips_active_and_already_extracted_conversations()
    {
        using var harness = new Harness("unused");
        // Còn nóng (idle < 15 phút) — chưa trích.
        await harness.SeedConversationAsync(lastMessageAt: Now.AddMinutes(-5));
        // Đã trích sau tin cuối — không trích lại.
        await harness.SeedConversationAsync(lastMessageAt: Now.AddHours(-2), memoryExtractedAt: Now.AddHours(-1));

        await harness.Job.RunScanAsync();

        (await harness.Db.ContactMemories.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        harness.Chat.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Extraction_failure_keeps_watermark_for_retry()
    {
        using var harness = new Harness("rác 1", "rác 2", "rác 3"); // extractor chịu thua -> null
        await harness.SeedConversationAsync(lastMessageAt: Now.AddMinutes(-30));

        await harness.Job.RunScanAsync();

        var conv = await harness.Db.Conversations.IgnoreQueryFilters().SingleAsync();
        conv.MemoryExtractedAt.Should().BeNull(); // lượt sau quét lại
        (await harness.Db.ContactMemories.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TestAppDb _testDb;

        public Harness(params string[] chatResponses)
        {
            _testDb = new TestAppDb();
            Chat = new ScriptedChatClient();
            Chat.Script(chatResponses.Where(r => r.Length > 0).ToArray());
            var pii = Substitute.For<IPiiRedactor>();
            pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => new RedactionResult(call.Arg<string>(), []));
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);

            Job = new ContactMemoryExtractionJob(
                Db,
                new ContactFactExtractor(Chat, new NoopLlmScope()),
                pii,
                clock,
                Options.Create(new LearningOptions()),
                NullLogger<ContactMemoryExtractionJob>.Instance);
        }

        public Clawbot.Infrastructure.Persistence.AppDbContext Db => _testDb.Db;
        public Guid TenantId => _testDb.TenantId;
        public ContactMemoryExtractionJob Job { get; }
        public ScriptedChatClient Chat { get; }

        public async Task<Guid> SeedConversationAsync(DateTimeOffset lastMessageAt, DateTimeOffset? memoryExtractedAt = null)
        {
            var contact = Contact.Create(TenantId, "Khách test", Now.AddDays(-10));
            Db.Set<Contact>().Add(contact);

            var conv = Conversation.Open(TenantId, "zalo", $"thread-{Guid.NewGuid():N}", Now.AddDays(-1), contact.Id);
            conv.AppendMessage("in", "contact", "em học HSK3 rồi, ca tối 2-4-6 nhé", "text", lastMessageAt);
            if (memoryExtractedAt is { } extractedAt) conv.MarkMemoryExtracted(extractedAt);
            Db.Conversations.Add(conv);
            await Db.SaveChangesAsync();
            return contact.Id;
        }

        public void Dispose() => _testDb.Dispose();
    }

    public sealed class ScriptedChatClient : IClaudeChatClient
    {
        private readonly Queue<string> _responses = new();
        public int Calls { get; private set; }

        public void Script(params string[] responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ClaudeReply(
                _responses.Count > 0 ? _responses.Dequeue() : "rác",
                1, 1, 0.01m, "test"));
        }

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
