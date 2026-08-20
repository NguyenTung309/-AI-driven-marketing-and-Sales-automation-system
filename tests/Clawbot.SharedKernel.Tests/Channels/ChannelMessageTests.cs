using Clawbot.SharedKernel.Channels;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Channels;

public sealed class ChannelMessageTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 8, 17, 9, 30, 0, TimeSpan.FromHours(7));

    private static ChannelMessage Make(
        string messageType = "text",
        string? parentPostId = null,
        string? parentCommentId = null) =>
        new(
            "facebook",
            "thread-1",
            "psid-9",
            "Cho em hỏi học phí",
            SentAt,
            new Dictionary<string, string> { ["page_id"] = "p-1" },
            messageType,
            parentPostId,
            AttachmentUrl: null,
            parentCommentId);

    [Fact]
    public void Constructor_SetsRequiredFields()
    {
        var message = Make();

        message.Channel.Should().Be("facebook");
        message.ExternalThreadId.Should().Be("thread-1");
        message.ExternalUserId.Should().Be("psid-9");
        message.Text.Should().Be("Cho em hỏi học phí");
        message.SentAt.Should().Be(SentAt);
        message.Metadata.Should().ContainKey("page_id");
    }

    [Fact]
    public void Defaults_TreatMessageAsPlainDirectMessage()
    {
        var message = Make();

        message.MessageType.Should().Be("text");
        message.ParentPostId.Should().BeNull();
        message.AttachmentUrl.Should().BeNull();
        message.ParentCommentId.Should().BeNull();
    }

    [Fact]
    public void CommentMessage_CarriesPostAndParentComment()
    {
        var message = Make("comment", parentPostId: "post-77", parentCommentId: "cmt-5");

        message.MessageType.Should().Be("comment");
        message.ParentPostId.Should().Be("post-77");
        message.ParentCommentId.Should().Be("cmt-5");
    }

    [Fact]
    public void WithExpression_LeavesOriginalUnchanged()
    {
        var original = Make();
        var updated = original with { Text = "Đã rõ, cảm ơn" };

        original.Text.Should().Be("Cho em hỏi học phí");
        updated.Text.Should().Be("Đã rõ, cảm ơn");
        updated.ExternalThreadId.Should().Be(original.ExternalThreadId);
    }
}

public sealed class ChannelInboundMessageReceivedTests
{
    [Fact]
    public void Constructor_SetsTenantAndMessage()
    {
        var tenantId = Guid.NewGuid();
        var message = new ChannelMessage(
            "zalo",
            "t-2",
            "u-2",
            "xin chào",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>());

        var evt = new ChannelInboundMessageReceived(tenantId, message);

        evt.TenantId.Should().Be(tenantId);
        evt.Message.Should().BeSameAs(message);
    }
}
