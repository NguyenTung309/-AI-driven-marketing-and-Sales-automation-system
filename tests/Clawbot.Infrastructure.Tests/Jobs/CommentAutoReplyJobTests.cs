using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Contacts;
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
    public async Task RunAsync_replies_under_comment_and_sends_private_reply()
    {
        // Fix comment-auto-reply: rep công khai qua action reply_comment (đúng comment id) + DM riêng
        // qua private_replies (post_id + comment id + from_id của người comment).
        using var fx = new TestAppDb();
        var contact = Contact.Create(fx.TenantId, "Khach Comment", Now.AddDays(-1));
        contact.LinkExternalId("facebook", "fb-user-77", Now.AddDays(-1));
        var conversation = Conversation.Open(fx.TenantId, "facebook", "page-1:comment-conv-1", Now, contact.Id);
        var inbound = conversation.AppendMessage(
            "in", "contact", "Gia HSK4 bao nhieu?", "text", Now,
            externalMessageId: "comment-1",
            originalContent: "Gia HSK4 bao nhieu?", redactedContent: "Gia HSK4 bao nhieu?",
            messageType: "comment", parentPostId: "post-99");
        fx.Db.Contacts.Add(contact);
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var adapter = new FakeCommentAdapter();
        var sut = BuildJob(fx, adapter);

        await sut.RunAsync(fx.TenantId, inbound.Id, CancellationToken.None);

        adapter.CommentReplies.Should().ContainSingle()
            .Which.Should().Be(("page-1:comment-conv-1", "comment-1"));
        adapter.PrivateReplies.Should().ContainSingle()
            .Which.Should().Be(("page-1:comment-conv-1", "post-99", "comment-1", "fb-user-77"));
        fx.Db.ChangeTracker.Clear();
        var outbound = await fx.Db.Messages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversation.Id && m.Direction == "out")
            .ToListAsync();
        outbound.Should().ContainSingle(m => m.MessageType == "comment" && m.ParentPostId == "post-99");
        outbound.Should().ContainSingle(m => m.MessageType == "dm" && m.ParentPostId == "post-99");
    }

    [Fact]
    public async Task RunAsync_skips_private_reply_when_contact_external_id_missing()
    {
        // Không có from_id → chỉ rep công khai, không DM (private_replies đòi from_id).
        using var fx = new TestAppDb();
        var conversation = Conversation.Open(fx.TenantId, "facebook", "page-1:comment-conv-3", Now, contactId: null);
        var inbound = conversation.AppendMessage(
            "in", "contact", "Gia bao nhieu?", "text", Now,
            externalMessageId: "comment-3", originalContent: "Gia bao nhieu?",
            redactedContent: "Gia bao nhieu?", messageType: "comment", parentPostId: "post-101");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var adapter = new FakeCommentAdapter();
        var sut = BuildJob(fx, adapter);

        await sut.RunAsync(fx.TenantId, inbound.Id, CancellationToken.None);

        adapter.CommentReplies.Should().HaveCount(1);
        adapter.PrivateReplies.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_skips_when_conversation_in_manual_mode()
    {
        // Review-gate P3: sale đang cầm hội thoại (AiAutoReplyEnabled=false) → bot không tự bắn comment/DM.
        using var fx = new TestAppDb();
        var conversation = Conversation.Open(fx.TenantId, "facebook", "page-1:comment-conv-2", Now, contactId: null);
        conversation.SetAiAutoReply(false);
        var inbound = conversation.AppendMessage(
            "in", "contact", "Gia HSK4 bao nhieu?", "text", Now,
            externalMessageId: "comment-2", originalContent: "Gia HSK4 bao nhieu?",
            redactedContent: "Gia HSK4 bao nhieu?", messageType: "comment", parentPostId: "post-100");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var adapter = new FakeCommentAdapter();
        var sut = BuildJob(fx, adapter);

        await sut.RunAsync(fx.TenantId, inbound.Id, CancellationToken.None);

        adapter.CommentReplies.Should().BeEmpty();
        adapter.PrivateReplies.Should().BeEmpty();
        fx.Db.ChangeTracker.Clear();
        (await fx.Db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == conversation.Id && m.Direction == "out"))
            .Should().Be(0);
    }

    [Fact]
    public async Task RunScanAsync_picks_up_recent_comment_and_is_idempotent()
    {
        // Đường polling: consumer bus không enqueue Hangfire được — scan quét comment mới rồi
        // tái dùng RunAsync; chạy 2 lần không nhân đôi nhờ alreadyReplied.
        using var fx = new TestAppDb();
        var contact = Contact.Create(fx.TenantId, "Khach Scan", Now.AddDays(-1));
        contact.LinkExternalId("facebook", "fb-user-88", Now.AddDays(-1));
        var conversation = Conversation.Open(fx.TenantId, "facebook", "page-1:comment-conv-4", Now, contact.Id);
        conversation.AppendMessage(
            "in", "contact", "Muon mua khoa hoc", "text", Now.AddMinutes(-5),
            externalMessageId: "comment-4", originalContent: "Muon mua khoa hoc",
            redactedContent: "Muon mua khoa hoc", messageType: "comment", parentPostId: "post-102");
        fx.Db.Contacts.Add(contact);
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var adapter = new FakeCommentAdapter();
        var sut = BuildJob(fx, adapter);

        await sut.RunScanAsync(CancellationToken.None);
        await sut.RunScanAsync(CancellationToken.None);

        adapter.CommentReplies.Should().HaveCount(1);
        adapter.PrivateReplies.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunAsync_skips_entirely_when_comment_adapter_missing()
    {
        // Host không có ICommentChannelAdapter → skip hẳn, KHÔNG fallback reply_inbox sai ngữ nghĩa.
        using var fx = new TestAppDb();
        var conversation = Conversation.Open(fx.TenantId, "facebook", "page-1:comment-conv-5", Now, contactId: null);
        var inbound = conversation.AppendMessage(
            "in", "contact", "Gia?", "text", Now,
            externalMessageId: "comment-5", originalContent: "Gia?",
            redactedContent: "Gia?", messageType: "comment", parentPostId: "post-103");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var sut = new CommentAutoReplyJob(
            fx.Db, new FixedIntentClassifier("ask_price", 0.75f),
            new FixedClock(Now.AddSeconds(10)), NullLogger<CommentAutoReplyJob>.Instance,
            commentAdapter: null);

        await sut.RunAsync(fx.TenantId, inbound.Id, CancellationToken.None);

        fx.Db.ChangeTracker.Clear();
        (await fx.Db.Messages.IgnoreQueryFilters().CountAsync(m => m.Direction == "out")).Should().Be(0);
    }

    private static CommentAutoReplyJob BuildJob(TestAppDb fx, FakeCommentAdapter adapter) =>
        new(fx.Db, new FixedIntentClassifier("ask_price", 0.75f),
            new FixedClock(Now.AddSeconds(10)), NullLogger<CommentAutoReplyJob>.Instance, adapter);

    private sealed class FakeCommentAdapter : ICommentChannelAdapter
    {
        public List<(string Thread, string CommentId)> CommentReplies { get; } = [];
        public List<(string Thread, string PostId, string CommentId, string FromId)> PrivateReplies { get; } = [];

        public Task<string?> SendCommentReplyAsync(string externalThreadId, string commentMessageId, string text, CancellationToken ct = default)
        {
            CommentReplies.Add((externalThreadId, commentMessageId));
            return Task.FromResult<string?>("cmt-reply-" + CommentReplies.Count);
        }

        public Task<string?> SendPrivateReplyAsync(string externalThreadId, string postId, string commentMessageId, string fromId, string text, CancellationToken ct = default)
        {
            PrivateReplies.Add((externalThreadId, postId, commentMessageId, fromId));
            return Task.FromResult<string?>("pm-reply-" + PrivateReplies.Count);
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
