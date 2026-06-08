using Clawbot.Agents.Core.Skills.Nlp;
using FluentAssertions;
using NSubstitute;
using Clawbot.Agents.Core.Chat;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Agents.Tests.Skills;

// M11 P1 — ClaudeConversationSummarizer (Claude → SummaryResult).
public sealed class ClaudeConversationSummarizerTests
{
    [Fact]
    public async Task Empty_turns_returns_empty()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        var sut = new ClaudeConversationSummarizer(claude, Options.Create(new SummarizerOptions()));

        var result = await sut.SummarizeAsync(Array.Empty<ConversationTurn>(), 50, CancellationToken.None);

        result.Summary.Should().BeEmpty();
        result.KeyPoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Parses_json_summary_and_key_points()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("""{"summary":"Customer asked about pricing","key_points":["Wants tuition info","Asked about schedule"]}""",
                100, 50, 0.001m));

        var sut = new ClaudeConversationSummarizer(claude, Options.Create(new SummarizerOptions()));
        var turns = new List<ConversationTurn>
        {
            new("user", "How much is the tuition?", DateTimeOffset.UtcNow),
            new("assistant", "Our tuition is 5M VND/month.", DateTimeOffset.UtcNow)
        };

        var result = await sut.SummarizeAsync(turns, 50, CancellationToken.None);

        result.Summary.Should().Be("Customer asked about pricing");
        result.KeyPoints.Should().HaveCount(2);
        result.KeyPoints[0].Should().Be("Wants tuition info");
    }

    [Fact]
    public async Task Null_turns_throws()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        var sut = new ClaudeConversationSummarizer(claude, Options.Create(new SummarizerOptions()));

        var act = async () => await sut.SummarizeAsync(null!, 50, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void ParseSummary_handles_non_json_gracefully()
    {
        var result = ClaudeConversationSummarizer.ParseSummary("Just a plain text summary");

        result.Summary.Should().Be("Just a plain text summary");
        result.KeyPoints.Should().BeEmpty();
    }
}

// M11 P1 — FastTextLanguageDetector (heuristic Unicode/diacritic).
public sealed class FastTextLanguageDetectorTests
{
    private readonly FastTextLanguageDetector _sut = new();

    [Fact]
    public async Task Vietnamese_diacritics_detect_vi()
    {
        var result = await _sut.DetectAsync("Xin chào, tôi muốn hỏi về học phí", CancellationToken.None);

        result.LanguageCode.Should().Be("vi");
        result.Confidence.Should().BeGreaterThan(0.3f);
    }

    [Fact]
    public async Task Chinese_characters_detect_zh()
    {
        var result = await _sut.DetectAsync("你好，我想问一下学费", CancellationToken.None);

        result.LanguageCode.Should().Be("zh");
        result.Confidence.Should().BeGreaterThan(0.3f);
    }

    [Fact]
    public async Task Basic_latin_detect_en()
    {
        var result = await _sut.DetectAsync("Hello, what is the tuition fee?", CancellationToken.None);

        result.LanguageCode.Should().Be("en");
        result.Confidence.Should().BeGreaterThan(0.1f);
    }

    [Fact]
    public async Task Empty_returns_unknown()
    {
        var result = await _sut.DetectAsync("", CancellationToken.None);

        result.LanguageCode.Should().Be("unknown");
        result.Confidence.Should().Be(0f);
    }

    [Fact]
    public async Task Japanese_hiragana_detect_ja()
    {
        var result = await _sut.DetectAsync("こんにちは、学費について質問があります", CancellationToken.None);

        result.LanguageCode.Should().Be("ja");
    }
}

// M11 P1 — DetoxifyToxicityFilter (heuristic lexicon).
public sealed class DetoxifyToxicityFilterTests
{
    private readonly DetoxifyToxicityFilter _sut = new();

    [Fact]
    public async Task Clean_text_low_scores()
    {
        var result = await _sut.ScoreAsync("Hello, I want to learn Chinese", CancellationToken.None);

        result.Toxicity.Should().BeLessThan(0.2f);
        result.Profanity.Should().Be(0f);
    }

    [Fact]
    public async Task Profanity_detected()
    {
        var result = await _sut.ScoreAsync("đéo hiểu gì cả", CancellationToken.None);

        result.Profanity.Should().BeGreaterThan(0f);
        result.Toxicity.Should().BeGreaterThan(0f);
    }

    [Fact]
    public async Task IsBlockedAsync_above_threshold_returns_true()
    {
        var blocked = await _sut.IsBlockedAsync("fuck you stupid idiot", threshold: 0.3f, CancellationToken.None);

        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task IsBlockedAsync_clean_text_returns_false()
    {
        var blocked = await _sut.IsBlockedAsync("Hello, how are you?", threshold: 0.7f, CancellationToken.None);

        blocked.Should().BeFalse();
    }

    [Fact]
    public async Task Threat_words_detected()
    {
        var result = await _sut.ScoreAsync("I will kill you", CancellationToken.None);

        result.Threat.Should().BeGreaterThan(0f);
    }

    [Fact]
    public async Task Empty_text_returns_zero()
    {
        var result = await _sut.ScoreAsync("", CancellationToken.None);

        result.Toxicity.Should().Be(0f);
        result.Insult.Should().Be(0f);
        result.Threat.Should().Be(0f);
    }
}
