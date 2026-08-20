using Clawbot.Agents.Core.Skills.Nlp;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Baseline intent: purchase_intent xếp trước ask_price; không khớp => other; rỗng => unknown.
public sealed class KeywordIntentClassifierTests
{
    private static KeywordIntentClassifier NewClassifier() => new();

    private static async Task<IntentResult> ClassifyAsync(string text)
        => await NewClassifier().ClassifyAsync(text, null, CancellationToken.None);

    [Fact]
    public void Name_IsIntentClassification()
    {
        NewClassifier().Name.Should().Be("intent-classification");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Classify_Blank_ReturnsUnknown(string text)
    {
        var result = await ClassifyAsync(text);

        result.Label.Should().Be("unknown");
        result.Confidence.Should().Be(0f);
    }

    [Fact]
    public async Task Classify_PurchaseIntent_RanksBeforePrice()
    {
        // "mua" (purchase) đứng trước "giá" (price) trong luật => purchase_intent thắng.
        var result = await ClassifyAsync("em muốn mua, giá bao nhiêu");

        result.Label.Should().Be("purchase_intent");
        result.Confidence.Should().Be(0.55f);
    }

    [Fact]
    public async Task Classify_PriceOnly_ReturnsAskPrice()
    {
        var result = await ClassifyAsync("học phí thế nào ạ");

        result.Label.Should().Be("ask_price");
    }

    [Theory]
    [InlineData("lịch học khi nào", "ask_schedule")]
    [InlineData("cho em đăng ký thử", "book_trial")]
    [InlineData("dịch vụ quá tệ", "complaint")]
    [InlineData("cho gặp người thật đi", "escalation")]
    [InlineData("xin chào shop", "greeting")]
    public async Task Classify_VariousIntents(string text, string expected)
    {
        (await ClassifyAsync(text)).Label.Should().Be(expected);
    }

    [Fact]
    public async Task Classify_NoKeyword_ReturnsOther()
    {
        var result = await ClassifyAsync("blah blah random text");

        result.Label.Should().Be("other");
        result.Confidence.Should().Be(0.30f);
    }

    [Fact]
    public async Task Classify_ChineseKeyword_Detected()
    {
        (await ClassifyAsync("你好")).Label.Should().Be("greeting");
    }
}
