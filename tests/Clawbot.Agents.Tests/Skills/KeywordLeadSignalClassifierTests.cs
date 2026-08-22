using Clawbot.Agents.Core.Skills.Lead;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Baseline keyword lead-signal: multi-label, dấu ? có nội dung => substantive, ack không tính.
public sealed class KeywordLeadSignalClassifierTests
{
    private static KeywordLeadSignalClassifier NewClassifier() => new();

    private static async Task<IReadOnlyList<string>> ClassifyAsync(string message)
        => (await NewClassifier().ClassifyAsync(message, null)).EventCodes;

    [Fact]
    public void Name_IsLeadSignalClassification()
    {
        NewClassifier().Name.Should().Be("lead-signal-classification");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Classify_Blank_ReturnsEmpty(string message)
    {
        (await ClassifyAsync(message)).Should().BeEmpty();
    }

    [Fact]
    public async Task Classify_PriceQuestion_TagsPriceAndSubstantive()
    {
        var codes = await ClassifyAsync("học phí bao nhiêu vậy shop?");

        codes.Should().Contain(LeadSignalCodes.AskedPrice);
        codes.Should().Contain(LeadSignalCodes.AskedSubstantiveQuestion);
    }

    [Fact]
    public async Task Classify_MultiLabel_ClassSizeAndPrice()
    {
        var codes = await ClassifyAsync("lớp mấy người, học phí nhiêu");

        codes.Should().Contain(LeadSignalCodes.AskedClassSize);
        codes.Should().Contain(LeadSignalCodes.AskedPrice);
    }

    [Fact]
    public async Task Classify_PurchaseIntent_Detected()
    {
        var codes = await ClassifyAsync("em chốt đơn nhé, chuyển khoản luôn");

        codes.Should().Contain(LeadSignalCodes.PurchaseIntent);
    }

    [Fact]
    public async Task Classify_AcknowledgementWithQuestionMark_NotSubstantive()
    {
        var codes = await ClassifyAsync("dạ ok?");

        codes.Should().NotContain(LeadSignalCodes.AskedSubstantiveQuestion);
    }

    [Fact]
    public async Task Classify_ChineseKeyword_Detected()
    {
        var codes = await ClassifyAsync("学费多少钱");

        codes.Should().Contain(LeadSignalCodes.AskedPrice);
    }

    [Fact]
    public async Task Classify_ResultCodesAreDistinct()
    {
        // "giá" và "học phí" cùng map AskedPrice — không được nhân đôi.
        var codes = await ClassifyAsync("giá và học phí thế nào");

        codes.Count(c => c == LeadSignalCodes.AskedPrice).Should().Be(1);
    }

    [Fact]
    public async Task Classify_ScheduleAndTeacher_Detected()
    {
        var codes = await ClassifyAsync("lịch học ra sao, ai dạy vậy");

        codes.Should().Contain(LeadSignalCodes.AskedSchedule);
        codes.Should().Contain(LeadSignalCodes.AskedTeacher);
    }

    [Fact]
    public async Task Classify_Commitment_Detected()
    {
        var codes = await ClassifyAsync("có cam kết đầu ra không");

        codes.Should().Contain(LeadSignalCodes.AskedCommitment);
    }
}
