using Clawbot.Agents.Core.Skills.Nlp;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Skills;

// M11 — RegexPiiRedactor (VN phone / email / 12-digit CCCD).
public sealed class RegexPiiRedactorTests
{
    private readonly RegexPiiRedactor _sut = new();

    [Fact]
    public async Task Redacts_vietnamese_phone()
    {
        var result = await _sut.RedactAsync("Call me at 0912345678 please", CancellationToken.None);

        result.RedactedText.Should().Be("Call me at [PHONE] please");
        result.Spans.Should().ContainSingle(s => s.Type == "phone");
    }

    [Fact]
    public async Task Redacts_international_vietnamese_phone_with_spaces()
    {
        var result = await _sut.RedactAsync("Call +84 912 345 678", CancellationToken.None);

        result.RedactedText.Should().Be("Call [PHONE]");
    }

    [Fact]
    public async Task Redacts_email()
    {
        var result = await _sut.RedactAsync("mail a@b.com", CancellationToken.None);

        result.RedactedText.Should().Be("mail [EMAIL]");
    }

    [Fact]
    public async Task Redacts_12_digit_id()
    {
        var result = await _sut.RedactAsync("CCCD 012345678901 ok", CancellationToken.None);

        result.RedactedText.Should().Be("CCCD [ID] ok");
    }

    [Fact]
    public async Task Redacts_multiple_pii_preserving_positions()
    {
        var result = await _sut.RedactAsync("p 0912345678 e a@b.com", CancellationToken.None);

        result.RedactedText.Should().Be("p [PHONE] e [EMAIL]");
        result.Spans.Should().HaveCount(2);
    }

    [Fact]
    public async Task Empty_input_returns_empty()
    {
        var result = await _sut.RedactAsync("", CancellationToken.None);

        result.RedactedText.Should().Be(string.Empty);
        result.Spans.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_input_returns_empty()
    {
        var result = await _sut.RedactAsync(null!, CancellationToken.None);

        result.RedactedText.Should().Be(string.Empty);
        result.Spans.Should().BeEmpty();
    }

    [Fact]
    public async Task Eleven_digits_not_treated_as_id()
    {
        var result = await _sut.RedactAsync("num 01234567890 end", CancellationToken.None);

        result.RedactedText.Should().Be("num 01234567890 end");
    }
}

// M11 — KeywordIntentClassifier (VI/EN/中 keyword buckets).
public sealed class KeywordIntentClassifierTests
{
    private readonly KeywordIntentClassifier _sut = new();

    [Theory]
    [InlineData("Khóa học giá bao nhiêu?", "ask_price")]
    [InlineData("hello there", "greeting")]
    [InlineData("tôi muốn gặp người thật", "escalation")]
    [InlineData("学费多少", "ask_price")]
    public async Task Classifies_known_intents(string text, string expected)
    {
        var result = await _sut.ClassifyAsync(text, null, CancellationToken.None);

        result.Label.Should().Be(expected);
        result.Confidence.Should().BeApproximately(0.55f, 0.0001f);
    }

    [Fact]
    public async Task Unknown_text_returns_other()
    {
        var result = await _sut.ClassifyAsync("zzz qqq", null, CancellationToken.None);

        result.Label.Should().Be("other");
        result.Confidence.Should().BeApproximately(0.30f, 0.0001f);
    }

    [Fact]
    public async Task Empty_text_returns_unknown()
    {
        var result = await _sut.ClassifyAsync("   ", null, CancellationToken.None);

        result.Label.Should().Be("unknown");
        result.Confidence.Should().Be(0f);
    }
}

// M11 — LexiconSentimentAnalyzer (positive/negative bag-of-words).
public sealed class LexiconSentimentAnalyzerTests
{
    private readonly LexiconSentimentAnalyzer _sut = new();

    [Fact]
    public async Task No_keywords_returns_neutral()
    {
        var result = await _sut.AnalyzeAsync("xyz abc", CancellationToken.None);

        result.Polarity.Should().Be("neutral");
        result.Confidence.Should().BeApproximately(0.40f, 0.0001f);
    }

    [Fact]
    public async Task Single_positive_word_returns_positive()
    {
        var result = await _sut.AnalyzeAsync("dịch vụ tốt", CancellationToken.None);

        result.Polarity.Should().Be("positive");
        result.Confidence.Should().BeApproximately(0.60f, 0.0001f);
    }

    [Fact]
    public async Task Single_negative_word_returns_negative()
    {
        var result = await _sut.AnalyzeAsync("sản phẩm tệ", CancellationToken.None);

        result.Polarity.Should().Be("negative");
        result.Confidence.Should().BeApproximately(0.60f, 0.0001f);
    }

    [Fact]
    public async Task Confidence_caps_at_090()
    {
        var result = await _sut.AnalyzeAsync("tốt tuyệt hay thích love", CancellationToken.None);

        result.Polarity.Should().Be("positive");
        result.Confidence.Should().BeApproximately(0.90f, 0.0001f);
    }

    [Fact]
    public async Task Tie_returns_neutral()
    {
        var result = await _sut.AnalyzeAsync("tốt nhưng tệ", CancellationToken.None);

        result.Polarity.Should().Be("neutral");
        result.Confidence.Should().BeApproximately(0.45f, 0.0001f);
    }

    [Fact]
    public async Task Empty_returns_neutral_zero()
    {
        var result = await _sut.AnalyzeAsync("", CancellationToken.None);

        result.Polarity.Should().Be("neutral");
        result.Confidence.Should().Be(0f);
    }
}
