using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Messaging;

public sealed class AiAutoReplyResumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Replies_WhenLastMessageIsUnansweredCustomerMessage()
    {
        // Arrange
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("out", "user", "sale tra loi tay", "text", Now.AddMinutes(-10));
        conv.AppendMessage("in", "contact", "<div>chi tiết hơn đi bạn</div>", "text", Now.AddMinutes(-5));
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var gateway = Substitute.For<IChatAutoReplyGateway>();
        var sut = new AiAutoReplyResumer(fx.Db, gateway, NullLogger<AiAutoReplyResumer>.Instance);

        // Act
        await sut.ReplyToHangingCustomerMessageAsync(fx.TenantId, conv.Id, CancellationToken.None);

        // Assert: HTML stripped, đúng conversation
        await gateway.Received(1).ReplyAsync(fx.TenantId, conv.Id, "chi tiết hơn đi bạn",
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replies_WhenLastOutboundIsRejectedBlockedDraft()
    {
        // Arrange: draft AI bị người từ chối (blocked, chưa bao giờ tới khách) là row mới nhất —
        // phải bị bỏ qua để tin khách trước đó vẫn được coi là đang treo.
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("in", "contact", "chi tiết hơn đi bạn", "text", Now.AddMinutes(-10));
        conv.AppendMessage("out", "agent", "draft bị từ chối", "text", Now.AddMinutes(-5), status: "blocked");
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var gateway = Substitute.For<IChatAutoReplyGateway>();
        var sut = new AiAutoReplyResumer(fx.Db, gateway, NullLogger<AiAutoReplyResumer>.Instance);

        // Act
        var triggered = await sut.ReplyToHangingCustomerMessageAsync(fx.TenantId, conv.Id, CancellationToken.None);

        // Assert: reply cho tin khách, history không chứa draft blocked
        triggered.Should().BeTrue();
        await gateway.Received(1).ReplyAsync(fx.TenantId, conv.Id, "chi tiết hơn đi bạn",
            Arg.Is<IReadOnlyList<string>>(h => !h.Contains("draft bị từ chối")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNothing_WhenLastMessageIsOutbound()
    {
        // Arrange: sale/AI đã trả lời — không có tin treo
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("in", "contact", "khách hỏi", "text", Now.AddMinutes(-10));
        conv.AppendMessage("out", "user", "sale đã trả lời", "text", Now.AddMinutes(-5));
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var gateway = Substitute.For<IChatAutoReplyGateway>();
        var sut = new AiAutoReplyResumer(fx.Db, gateway, NullLogger<AiAutoReplyResumer>.Instance);

        // Act
        await sut.ReplyToHangingCustomerMessageAsync(fx.TenantId, conv.Id, CancellationToken.None);

        // Assert
        await gateway.DidNotReceiveWithAnyArgs().ReplyAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task DoesNothing_WhenAiDisabled()
    {
        // Arrange
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("in", "contact", "khách hỏi", "text", Now.AddMinutes(-5));
        conv.SetAiAutoReply(false);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var gateway = Substitute.For<IChatAutoReplyGateway>();
        var sut = new AiAutoReplyResumer(fx.Db, gateway, NullLogger<AiAutoReplyResumer>.Instance);

        // Act
        await sut.ReplyToHangingCustomerMessageAsync(fx.TenantId, conv.Id, CancellationToken.None);

        // Assert
        await gateway.DidNotReceiveWithAnyArgs().ReplyAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task Skips_WhenPendingApprovalDraftExists()
    {
        // Arrange: draft chờ duyệt — không đẻ thêm reply
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("out", "agent", "draft chờ duyệt", "text", Now.AddMinutes(-10), status: "pending_approval");
        conv.AppendMessage("in", "contact", "khách hỏi tiếp", "text", Now.AddMinutes(-5));
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var gateway = Substitute.For<IChatAutoReplyGateway>();
        var sut = new AiAutoReplyResumer(fx.Db, gateway, NullLogger<AiAutoReplyResumer>.Instance);

        // Act
        await sut.ReplyToHangingCustomerMessageAsync(fx.TenantId, conv.Id, CancellationToken.None);

        // Assert
        await gateway.DidNotReceiveWithAnyArgs().ReplyAsync(default, default, default!, default!, default);
    }

    [Fact]
    public async Task DoesNotThrow_WhenGatewayFails()
    {
        // Arrange: best-effort — lỗi gRPC không được lan ra caller (toggle endpoint/sweep)
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now);
        conv.AppendMessage("in", "contact", "khách hỏi", "text", Now.AddMinutes(-5));
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var gateway = Substitute.For<IChatAutoReplyGateway>();
        gateway.ReplyAsync(default, default, default!, default!, default)
            .ReturnsForAnyArgs<Task>(_ => throw new InvalidOperationException("grpc down"));
        var sut = new AiAutoReplyResumer(fx.Db, gateway, NullLogger<AiAutoReplyResumer>.Instance);

        // Act
        var act = async () => await sut.ReplyToHangingCustomerMessageAsync(fx.TenantId, conv.Id, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
