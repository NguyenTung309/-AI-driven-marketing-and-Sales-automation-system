using Clawbot.Agents.Core.Skills.Lead;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Skills;

// Part C.2 — keyword lead-signal classifier (LLM fallback oracle) + LLM JSON parsing.
public sealed class KeywordLeadSignalClassifierTests
{
    private readonly KeywordLeadSignalClassifier _sut = new();

    [Fact]
    public async Task Classifies_class_size_question()
    {
        var result = await _sut.ClassifyAsync("Lớp sĩ số bao nhiêu người vậy ạ?", null, CancellationToken.None);

        result.EventCodes.Should().Contain(LeadSignalCodes.AskedClassSize);
        result.EventCodes.Should().Contain(LeadSignalCodes.AskedSubstantiveQuestion);
    }

    [Fact]
    public async Task Classifies_commitment_question()
    {
        var result = await _sut.ClassifyAsync("Trung tâm có cam kết đầu ra không?", null, CancellationToken.None);

        result.EventCodes.Should().Contain(LeadSignalCodes.AskedCommitment);
    }

    [Fact]
    public async Task Detects_multiple_signals_in_one_message()
    {
        var result = await _sut.ClassifyAsync("Học phí bao nhiêu và lịch học thế nào ạ?", null, CancellationToken.None);

        result.EventCodes.Should().Contain(LeadSignalCodes.AskedPrice);
        result.EventCodes.Should().Contain(LeadSignalCodes.AskedSchedule);
    }

    [Fact]
    public async Task Purchase_intent_detected()
    {
        var result = await _sut.ClassifyAsync("Cho em đăng ký luôn gói HSK4 ạ", null, CancellationToken.None);

        result.EventCodes.Should().Contain(LeadSignalCodes.PurchaseIntent);
    }

    [Theory]
    [InlineData("vâng ạ")]
    [InlineData("ok em")]
    [InlineData("để em xem đã")]
    public async Task Acknowledgements_yield_no_signal(string text)
    {
        var result = await _sut.ClassifyAsync(text, null, CancellationToken.None);

        result.EventCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_message_yields_no_signal()
    {
        var result = await _sut.ClassifyAsync("   ", null, CancellationToken.None);

        result.EventCodes.Should().BeEmpty();
    }
}

public sealed class ClaudeLeadSignalParseTests
{
    [Fact]
    public void Parses_known_codes_from_json_array()
    {
        var codes = ClaudeLeadSignalClassifier.ParseCodes("[\"asked_price\", \"asked_schedule\"]");

        codes.Should().BeEquivalentTo(new[] { LeadSignalCodes.AskedPrice, LeadSignalCodes.AskedSchedule });
    }

    [Fact]
    public void Ignores_unknown_codes()
    {
        var codes = ClaudeLeadSignalClassifier.ParseCodes("[\"asked_price\", \"made_up_code\"]");

        codes.Should().ContainSingle().Which.Should().Be(LeadSignalCodes.AskedPrice);
    }

    [Fact]
    public void Empty_array_yields_no_codes()
    {
        ClaudeLeadSignalClassifier.ParseCodes("[]").Should().BeEmpty();
    }

    [Fact]
    public void Deduplicates_repeated_codes()
    {
        var codes = ClaudeLeadSignalClassifier.ParseCodes("[\"asked_teacher\",\"asked_teacher\"]");

        codes.Should().ContainSingle().Which.Should().Be(LeadSignalCodes.AskedTeacher);
    }
}
