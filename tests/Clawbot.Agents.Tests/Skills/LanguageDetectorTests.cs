using Clawbot.Agents.Core.Skills.Nlp;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Heuristic language detector theo Unicode-block + dấu tiếng Việt + tỉ lệ CJK.
public sealed class LanguageDetectorTests
{
    private static FastTextLanguageDetector NewDetector() => new();

    private static async Task<LanguageDetection> DetectAsync(string text)
        => await NewDetector().DetectAsync(text, CancellationToken.None);

    [Fact]
    public void Name_IsLanguageDetection()
    {
        NewDetector().Name.Should().Be("language-detection");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Detect_BlankText_ReturnsUnknown(string text)
    {
        var result = await DetectAsync(text);

        result.LanguageCode.Should().Be("unknown");
        result.Confidence.Should().Be(0f);
    }

    [Fact]
    public async Task Detect_PunctuationOnly_ReturnsUnknown()
    {
        var result = await DetectAsync("... !!! ??? ,,,");

        result.LanguageCode.Should().Be("unknown");
    }

    [Fact]
    public async Task Detect_Vietnamese_ByDiacritics()
    {
        var result = await DetectAsync("Chào bạn, hôm nay trời đẹp quá phải không");

        result.LanguageCode.Should().Be("vi");
        result.Confidence.Should().BeGreaterThan(0f);
    }

    [Fact]
    public async Task Detect_PlainEnglish_ReturnsEnglish()
    {
        var result = await DetectAsync("Hello there, how are you doing today");

        result.LanguageCode.Should().Be("en");
    }

    [Fact]
    public async Task Detect_Chinese_ReturnsZh()
    {
        var result = await DetectAsync("你好世界这是一个测试消息");

        result.LanguageCode.Should().Be("zh");
    }

    [Fact]
    public async Task Detect_Japanese_ByKana_ReturnsJa()
    {
        var result = await DetectAsync("こんにちは、これはテストです");

        result.LanguageCode.Should().Be("ja");
    }

    [Fact]
    public async Task Detect_Korean_ReturnsKo()
    {
        var result = await DetectAsync("안녕하세요 이것은 테스트입니다");

        result.LanguageCode.Should().Be("ko");
    }

    [Fact]
    public async Task Detect_Thai_ReturnsTh()
    {
        var result = await DetectAsync("สวัสดีครับนี่คือการทดสอบ");

        result.LanguageCode.Should().Be("th");
    }

    [Fact]
    public async Task Detect_Confidence_NeverExceedsCeiling()
    {
        var result = await DetectAsync("你好世界你好世界你好世界你好世界");

        result.Confidence.Should().BeLessOrEqualTo(0.95f);
    }

    [Fact]
    public async Task Detect_DigitsOnly_FallsBackToEnglishLowConfidence()
    {
        // Không rune nào rơi vào block ngôn ngữ nào => best score thấp => fallback en 0.30.
        var result = await DetectAsync("1234567890");

        result.LanguageCode.Should().Be("en");
        result.Confidence.Should().Be(0.30f);
    }
}
