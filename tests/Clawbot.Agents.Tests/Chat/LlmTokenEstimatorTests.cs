using Clawbot.Agents.Core.Chat;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Chat;

// Bộ đếm token cục bộ dùng để ước lượng chi phí khi provider không trả usage.
// Kiểm cả nhánh tokenizer thật lẫn fallback ký tự, và bảng map model -> encoding.
public sealed class LlmTokenEstimatorTests
{
    [Fact]
    public void CountText_EmptyOrNull_ReturnsZero()
    {
        LlmTokenEstimator.CountText("gpt-5", null).Should().Be(0);
        LlmTokenEstimator.CountText("gpt-5", string.Empty).Should().Be(0);
    }

    [Fact]
    public void CountText_RealText_ReturnsPositiveCount()
    {
        // Với tokenizer thật, một câu tiếng Anh phải cho > 0 token.
        var count = LlmTokenEstimator.CountText("gpt-5", "The quick brown fox jumps over the lazy dog.");

        count.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("gpt-5")]
    [InlineData("gpt-4o")]
    [InlineData("gpt-4.1")]
    [InlineData("gpt-4.5")]
    [InlineData("o1-preview")]
    [InlineData("o3-mini")]
    [InlineData("o4")]
    [InlineData("gpt-4-turbo")]
    [InlineData("gpt-3.5-turbo")]
    [InlineData("text-embedding-3-small")]
    [InlineData("claude-opus-5")]
    [InlineData("")]
    public void CountText_KnownAndUnknownModels_ResolvesWithoutThrowing(string model)
    {
        // Mọi tên model (kể cả claude/lạ/rỗng) đều phải rơi về một encoding hợp lệ, không throw.
        var count = LlmTokenEstimator.CountText(model, "hello world");

        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountText_GatewayPrefixedModel_StillResolves()
    {
        // Gateway hay thêm tiền tố kiểu `cx/gpt-5.5-review`; phần sau dấu '/' phải khớp encoding.
        var prefixed = LlmTokenEstimator.CountText("cx/gpt-5.5-review", "hello world");
        var bare = LlmTokenEstimator.CountText("gpt-5.5", "hello world");

        prefixed.Should().Be(bare);
    }

    [Fact]
    public void CountPrompt_EmptyInputs_ReturnsPrimingOnly()
    {
        // Không system, history rỗng, không user message -> chỉ còn priming tokens (= 3).
        var total = LlmTokenEstimator.CountPrompt("gpt-5", systemPrompt: null, history: null, userMessage: null);

        total.Should().Be(3);
    }

    [Fact]
    public void CountPrompt_AddsPerMessageOverheadForEachSegment()
    {
        var system = "You are a helpful assistant.";
        var user = "What is the capital of France?";
        var history = new List<ChatTurn>
        {
            new("user", "Hi"),
            new("assistant", "Hello there"),
        };

        var total = LlmTokenEstimator.CountPrompt("gpt-5", system, history, user);

        // Tổng = priming(3) + sum(4 + text) cho system + 2 turn + user.
        var expected = 3
            + 4 + LlmTokenEstimator.CountText("gpt-5", system)
            + 4 + LlmTokenEstimator.CountText("gpt-5", "Hi")
            + 4 + LlmTokenEstimator.CountText("gpt-5", "Hello there")
            + 4 + LlmTokenEstimator.CountText("gpt-5", user);
        total.Should().Be(expected);
    }

    [Fact]
    public void CountPrompt_SkipsEmptySystemAndUser()
    {
        var history = new List<ChatTurn> { new("user", "only turn") };

        var total = LlmTokenEstimator.CountPrompt("gpt-5", systemPrompt: "", history: history, userMessage: "");

        var expected = 3 + 4 + LlmTokenEstimator.CountText("gpt-5", "only turn");
        total.Should().Be(expected);
    }
}
