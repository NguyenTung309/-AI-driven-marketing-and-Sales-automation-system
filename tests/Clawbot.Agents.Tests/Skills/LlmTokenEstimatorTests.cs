using Clawbot.Agents.Core.Chat;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Ước lượng token cục bộ khi provider không trả usage: tokenizer thật + fallback ký tự.
public sealed class LlmTokenEstimatorTests
{
    private const string Model = "gpt-4o";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CountText_EmptyOrNull_Zero(string? text)
    {
        LlmTokenEstimator.CountText(Model, text).Should().Be(0);
    }

    [Fact]
    public void CountText_NonEmpty_Positive()
    {
        LlmTokenEstimator.CountText(Model, "Hello world, this is a test.")
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountText_UnknownModel_UsesFallbackAndStaysPositive()
    {
        // Model lạ vẫn map về o200k proxy; luôn > 0 với text không rỗng.
        LlmTokenEstimator.CountText("some-exotic-model-xyz", "abcdefgh")
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountText_GatewayPrefixedModel_Resolves()
    {
        LlmTokenEstimator.CountText("cx/gpt-5.5-review", "hello there friend")
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountPrompt_EmptyEverything_ReturnsPrimingOnly()
    {
        // Không system, history, user => chỉ còn priming tokens (3).
        LlmTokenEstimator.CountPrompt(Model, null, null, null).Should().Be(3);
    }

    [Fact]
    public void CountPrompt_AddsPerMessageOverhead()
    {
        var withUser = LlmTokenEstimator.CountPrompt(Model, null, null, "hi");
        var empty = LlmTokenEstimator.CountPrompt(Model, null, null, null);

        // Thêm 1 message => cộng ít nhất overhead khung (4) + token nội dung.
        (withUser - empty).Should().BeGreaterThan(4);
    }

    [Fact]
    public void CountPrompt_IncludesSystemHistoryAndUser()
    {
        var history = new List<ChatTurn>
        {
            new("user", "câu hỏi đầu tiên"),
            new("assistant", "câu trả lời"),
        };

        var full = LlmTokenEstimator.CountPrompt(Model, "system prompt", history, "tin nhắn mới");
        var justUser = LlmTokenEstimator.CountPrompt(Model, null, null, "tin nhắn mới");

        full.Should().BeGreaterThan(justUser);
    }

    [Theory]
    [InlineData("gpt-3.5-turbo")]      // cl100k
    [InlineData("gpt-4-turbo")]        // cl100k
    [InlineData("text-embedding-3")]   // cl100k
    [InlineData("o1-preview")]         // o200k
    public void CountText_VariousEncodingFamilies_Positive(string model)
    {
        LlmTokenEstimator.CountText(model, "some sample content here")
            .Should().BeGreaterThan(0);
    }
}
