using Clawbot.Infrastructure.Messaging;
using FluentAssertions;
using static Clawbot.Infrastructure.Messaging.AiAutoReplyResumer;

namespace Clawbot.Infrastructure.Tests;

// BuildTrailingCustomerBlock: input mới nhất trước (đã loại out+blocked), output là khối tin khách
// cuối hội thoại gộp thành 1 userText + history phía trước — xem AiAutoReplyResumer.
public sealed class AiAutoReplyTrailingBlockTests
{
    private static ReplyContextMessage In(string content, string? type = "text") => new("in", type, content);
    private static ReplyContextMessage Out(string content) => new("out", "text", content);

    [Fact]
    public void MergesConsecutiveCustomerMessagesOldestFirst()
    {
        var newestFirst = new[]
        {
            In("có ưu đãi gì không ạ"),
            In("cho em hỏi khóa học giá bao nhiêu"),
            In("xin chào"),
        };

        var result = BuildTrailingCustomerBlock(newestFirst, historyLimit: 10);

        result.Should().NotBeNull();
        result!.Value.UserText.Should().Be("xin chào\ncho em hỏi khóa học giá bao nhiêu\ncó ưu đãi gì không ạ");
        result.Value.History.Should().BeEmpty();
    }

    [Fact]
    public void BlockStopsAtLastOutboundMessage_EarlierMessagesBecomeHistory()
    {
        var newestFirst = new[]
        {
            In("còn ưu đãi không"),
            In("giá bao nhiêu"),
            Out("Chào bạn, mình hỗ trợ gì được ạ?"),
            In("xin chào"),
        };

        var result = BuildTrailingCustomerBlock(newestFirst, historyLimit: 10);

        result.Should().NotBeNull();
        result!.Value.UserText.Should().Be("giá bao nhiêu\ncòn ưu đãi không");
        result.Value.History.Should().Equal("xin chào", "Chào bạn, mình hỗ trợ gì được ạ?");
    }

    [Fact]
    public void ReturnsNullWhenLastMessageIsOutbound()
    {
        var newestFirst = new[]
        {
            Out("Đã trả lời rồi"),
            In("câu hỏi cũ"),
        };

        BuildTrailingCustomerBlock(newestFirst, historyLimit: 10).Should().BeNull();
    }

    [Fact]
    public void ReturnsNullWhenNoMessages()
    {
        BuildTrailingCustomerBlock(Array.Empty<ReplyContextMessage>(), historyLimit: 10).Should().BeNull();
    }

    [Fact]
    public void ReturnsNullWhenTrailingBlockIsOnlyComments()
    {
        var newestFirst = new[]
        {
            In("comment hay quá", type: "comment"),
            Out("reply cũ"),
        };

        BuildTrailingCustomerBlock(newestFirst, historyLimit: 10).Should().BeNull();
    }

    [Fact]
    public void SkipsCommentsInsideBlockButKeepsChatMessages()
    {
        var newestFirst = new[]
        {
            In("giá bao nhiêu"),
            In("comment ngoài lề", type: "comment"),
        };

        var result = BuildTrailingCustomerBlock(newestFirst, historyLimit: 10);

        result.Should().NotBeNull();
        result!.Value.UserText.Should().Be("giá bao nhiêu");
    }

    [Fact]
    public void ReturnsNullWhenTrailingBlockIsWhitespaceOnly()
    {
        var newestFirst = new[]
        {
            In("   "),
            Out("reply cũ"),
        };

        BuildTrailingCustomerBlock(newestFirst, historyLimit: 10).Should().BeNull();
    }

    [Fact]
    public void StripsHtmlFromCustomerMessages()
    {
        var newestFirst = new[]
        {
            In("<div>có ưu đãi không &amp; học phí?</div>"),
            In("<div>xin chào</div>"),
        };

        var result = BuildTrailingCustomerBlock(newestFirst, historyLimit: 10);

        result.Should().NotBeNull();
        result!.Value.UserText.Should().Be("xin chào\ncó ưu đãi không & học phí?");
    }

    [Fact]
    public void HistoryRespectsLimitAndKeepsRawContent()
    {
        var newestFirst = new List<ReplyContextMessage> { In("tin mới") };
        for (var i = 0; i < 15; i++)
        {
            newestFirst.Add(Out($"reply {i}"));
        }

        var result = BuildTrailingCustomerBlock(newestFirst, historyLimit: 10);

        result.Should().NotBeNull();
        result!.Value.History.Should().HaveCount(10);
        // Oldest-first trong giới hạn: 10 tin gần khối nhất là reply 9..0 (mới nhất đứng cuối).
        result.Value.History[^1].Should().Be("reply 0");
        result.Value.History[0].Should().Be("reply 9");
    }
}
