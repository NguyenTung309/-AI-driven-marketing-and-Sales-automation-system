using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

// Review-gate P2 tầng 1 (QĐ2 tiered): deterministic trigger quyết định reply nào cần LLM critic.
public sealed class ChatReplyReviewTriggerTests
{
    private static ChatAgentReply Reply(string text, bool escalate = false, bool blocked = false) =>
        new(text, Array.Empty<RagChunk>(), 1, 1, 0m, 10, Intent: "pricing",
            Blocked: blocked, BlockReason: blocked ? "x" : null, Escalate: escalate);

    [Fact]
    public void Escalate_flag_triggers_review() =>
        ChatReplyReviewTrigger.NeedsLlmReview(Reply("Chào anh!", escalate: true)).Should().BeTrue();

    [Theory]
    [InlineData("Học phí khóa HSK4 là 5.500.000 đồng")]
    [InlineData("Cam kết đầu ra HSK5 sau 6 tháng")]
    [InlineData("Giảm 20% khi đăng ký hôm nay")]
    [InlineData("Uu dai hoc phi thang nay")]
    public void Risky_content_triggers_review(string text) =>
        ChatReplyReviewTrigger.NeedsLlmReview(Reply(text)).Should().BeTrue();

    [Theory]
    [InlineData("Chào anh, em có thể giúp gì ạ?")]
    [InlineData("Dạ trung tâm mở cửa cả tuần ạ.")]
    public void Plain_smalltalk_does_not_trigger(string text) =>
        ChatReplyReviewTrigger.NeedsLlmReview(Reply(text)).Should().BeFalse();

    [Fact]
    public void Blocked_reply_never_triggers() =>
        ChatReplyReviewTrigger.NeedsLlmReview(Reply("Giá 5 triệu", escalate: true, blocked: true)).Should().BeFalse();

    [Fact]
    public void Empty_reply_never_triggers() =>
        ChatReplyReviewTrigger.NeedsLlmReview(Reply("", escalate: true)).Should().BeFalse();
}
