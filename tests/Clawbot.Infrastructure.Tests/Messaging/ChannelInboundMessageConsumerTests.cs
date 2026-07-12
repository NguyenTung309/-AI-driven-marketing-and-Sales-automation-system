using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Messaging;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Messaging;

public sealed class ChannelInboundMessageConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);

    private static ChannelMessage Msg(IReadOnlyDictionary<string, string>? meta = null, string text = "hello") =>
        new("zalo", "page1:conv1", "user1", text, Now, meta ?? new Dictionary<string, string> { ["external_message_id"] = "m1" });

    private static ConsumeContext<ChannelInboundMessageReceived> Context(Guid tenantId, ChannelMessage msg)
    {
        var context = Substitute.For<ConsumeContext<ChannelInboundMessageReceived>>();
        context.Message.Returns(new ChannelInboundMessageReceived(tenantId, msg));
        return context;
    }

    [Fact]
    public async Task Consume_ForwardsMessageToIngestor()
    {
        using var fx = new TestAppDb();
        var channelMsg = Msg();
        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(fx.TenantId, channelMsg, Arg.Any<CancellationToken>())
            .Returns(new IngestResult(Guid.NewGuid(), Guid.NewGuid(), false));

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(),
            Substitute.For<IChatAutoReplyGateway>(), NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(Context(fx.TenantId, channelMsg));

        await ingestor.Received(1).IngestAsync(fx.TenantId, channelMsg, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PropagatesIngestorFailure_ForRetry()
    {
        using var fx = new TestAppDb();
        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task<IngestResult>>(_ => throw new InvalidOperationException("db down"));

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(),
            Substitute.For<IChatAutoReplyGateway>(), NullLogger<ChannelInboundMessageConsumer>.Instance);

        // Exception must bubble so MassTransit retry/error-queue policy applies
        var act = async () => await sut.Consume(Context(fx.TenantId, Msg()));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Consume_TriggersAutoReply_WhenFlagOn()
    {
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("in", "contact", "hello", "text", Now);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns(new IngestResult(conv.Id, Guid.NewGuid(), false));
        var gateway = Substitute.For<IChatAutoReplyGateway>();
        var notifier = Substitute.For<IInboxNotifier>();

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, notifier, gateway,
            NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(Context(fx.TenantId, Msg()));

        await gateway.Received(1).ReplyAsync(fx.TenantId, conv.Id, "hello",
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await notifier.Received(1).NotifyConversationUpdatedAsync(fx.TenantId, Arg.Any<InboxConversationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SkipsAutoReply_WhenFlagOff()
    {
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.SetAiAutoReply(false);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns(new IngestResult(conv.Id, Guid.NewGuid(), false));
        var gateway = Substitute.For<IChatAutoReplyGateway>();

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(), gateway,
            NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(Context(fx.TenantId, Msg()));

        await gateway.DidNotReceive().ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SkipsChatAutoReply_ForCommentMessage()
    {
        // Comment thread: chat auto-reply (reply_inbox) sai ngữ nghĩa — CommentAutoReplyJob scan lo.
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "facebook", "page1:conv1", Now);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns(new IngestResult(conv.Id, Guid.NewGuid(), false));
        var gateway = Substitute.For<IChatAutoReplyGateway>();

        var commentMsg = new ChannelMessage("facebook", "page1:conv1", "user1", "gia bao nhieu?", Now,
            new Dictionary<string, string> { ["external_message_id"] = "cmt1" },
            MessageType: "comment", ParentPostId: "post-1");
        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(), gateway,
            NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(Context(fx.TenantId, commentMsg));

        await gateway.DidNotReceive().ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SkipsAutoReply_WhenPendingDraftAwaitsApproval()
    {
        // Approval mode: 1 AI draft đang chờ duyệt (pending_approval) thì tin khách kế tiếp KHÔNG
        // được đẻ thêm draft — tránh xếp chồng nhiều reply pending cho cùng hội thoại.
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("out", "agent", "draft chờ duyệt", "text", Now, status: "pending_approval");
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns(new IngestResult(conv.Id, Guid.NewGuid(), false));
        var gateway = Substitute.For<IChatAutoReplyGateway>();

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(), gateway,
            NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(Context(fx.TenantId, Msg()));

        await gateway.DidNotReceive().ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SkipsAutoReply_WhenIngestDeduplicated()
    {
        // Idempotency (review-gate P2): MassTransit redelivery của cùng inbound → ingest dedup
        // → tuyệt đối không sinh reply/draft lần hai.
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns(new IngestResult(conv.Id, null, Deduplicated: true));
        var gateway = Substitute.For<IChatAutoReplyGateway>();

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(), gateway,
            NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(Context(fx.TenantId, Msg()));

        await gateway.DidNotReceive().ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SkipsAutoReply_ForOwnerEcho()
    {
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns(new IngestResult(conv.Id, Guid.NewGuid(), false));
        var gateway = Substitute.For<IChatAutoReplyGateway>();

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(), gateway,
            NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(Context(fx.TenantId, Msg(new Dictionary<string, string>
        {
            ["external_message_id"] = "m2",
            ["is_owner"] = "true",
        })));

        await gateway.DidNotReceive().ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_GatewayFailure_DoesNotFailIngest()
    {
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns(new IngestResult(conv.Id, Guid.NewGuid(), false));
        var gateway = Substitute.For<IChatAutoReplyGateway>();
        gateway.ReplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("agent down"));

        var sut = new ChannelInboundMessageConsumer(ingestor, fx.Db, Substitute.For<IInboxNotifier>(), gateway,
            NullLogger<ChannelInboundMessageConsumer>.Instance);

        // Best-effort: reply failure must not bubble (would redeliver + dedup -> reply lost anyway)
        var act = async () => await sut.Consume(Context(fx.TenantId, Msg()));
        await act.Should().NotThrowAsync();
    }
}
