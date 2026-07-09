using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class CommentAutoReplyJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_replies_and_sends_dm_for_purchase_comment()
    {
        using var fx = new TestAppDb();
        var conversation = Conversation.Open(
            fx.TenantId,
            "facebook",
            "page-1:comment-conv-1",
            Now,
            contactId: null);
        var inbound = conversation.AppendMessage(
            "in",
            "contact",
            "Gia HSK4 bao nhieu?",
            "text",
            Now,
            externalMessageId: "comment-1",
            originalContent: "Gia HSK4 bao nhieu?",
            redactedContent: "Gia HSK4 bao nhieu?",
            messageType: "comment",
            parentPostId: "post-99");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var adapter = new CapturingChannelAdapter();
        var sut = new CommentAutoReplyJob(
            fx.Db,
            adapter,
            new FixedIntentClassifier("ask_price", 0.75f),
            new FixedClock(Now.AddSeconds(10)),
            NullLogger<CommentAutoReplyJob>.Instance);

        await sut.RunAsync(fx.TenantId, inbound.Id, CancellationToken.None);

        adapter.Sends.Should().HaveCount(2);
        adapter.Sends.Should().OnlyContain(s => s.ExternalThreadId == "page-1:comment-conv-1");
        fx.Db.ChangeTracker.Clear();
        var outbound = await fx.Db.Messages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversation.Id && m.Direction == "out")
            .ToListAsync();
        outbound.Should().ContainSingle(m => m.MessageType == "comment" && m.ParentPostId == "post-99");
        outbound.Should().ContainSingle(m => m.MessageType == "dm" && m.ParentPostId == "post-99");
    }

    private sealed class CapturingChannelAdapter : IChannelAdapter
    {
        public string Name => "pancake";
        public List<(string ExternalThreadId, string Text)> Sends { get; } = [];

        public Task<bool> VerifyWebhookSignatureAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChannelMessage>>([]);

        public Task<string?> SendAsync(string externalThreadId, string text, CancellationToken ct = default)
        {
            Sends.Add((externalThreadId, text));
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FixedIntentClassifier(string label, float confidence) : IIntentClassifier
    {
        public string Name => "fixed-intent";

        public Task<IntentResult> ClassifyAsync(string text, string? locale, CancellationToken ct) =>
            Task.FromResult(new IntentResult(label, confidence));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
