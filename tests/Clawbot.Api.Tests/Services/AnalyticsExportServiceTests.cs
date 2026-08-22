using System.Text;
using Clawbot.Api.Contracts.Analytics;
using Clawbot.Api.Services;
using FluentAssertions;

namespace Clawbot.Api.Tests.Services;

public sealed class AnalyticsExportServiceTests
{
    private static OmniChannelRowDto Row(
        string platform = "facebook",
        decimal? avgResponse = 12.5m) =>
        new(platform, Leads: 10, Dms: 25, Replies: 20, RepliedDms: 18, Conversions: 4, avgResponse);

    [Theory]
    [InlineData(null, "csv")]
    [InlineData("", "csv")]
    [InlineData("   ", "csv")]
    [InlineData("CSV", "csv")]
    [InlineData("  PDF  ", "pdf")]
    public void NormalizeFormat_ValidInput_LowercasesAndDefaults(string? input, string expected)
    {
        AnalyticsExportService.NormalizeFormat(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("json")]
    public void NormalizeFormat_UnsupportedFormat_Throws(string input)
    {
        var act = () => AnalyticsExportService.NormalizeFormat(input);

        act.Should().Throw<ArgumentException>().WithParameterName("format");
    }

    [Fact]
    public void BuildCsv_WritesFixedHeader()
    {
        var csv = AnalyticsExportService.BuildCsv([]);

        csv.Should().Be("platform,leads,dms,replies,conversions,avg_response_time_sec\r\n");
    }

    [Fact]
    public void BuildCsv_WritesRowValues()
    {
        var csv = AnalyticsExportService.BuildCsv([Row()]);

        csv.Should().Contain("facebook,10,25,20,4,12.5\r\n");
    }

    [Fact]
    public void BuildCsv_NullAvgResponse_EmitsEmptyCell()
    {
        var csv = AnalyticsExportService.BuildCsv([Row(avgResponse: null)]);

        csv.Should().EndWith("facebook,10,25,20,4,\r\n");
    }

    [Fact]
    public void BuildCsv_RoundsAvgResponseToTwoDecimals()
    {
        var csv = AnalyticsExportService.BuildCsv([Row(avgResponse: 12.3456m)]);

        csv.Should().Contain(",12.35\r\n");
    }

    [Theory]
    [InlineData("a,b")]
    [InlineData("nói \"chào\"")]
    [InlineData("dòng1\ndòng2")]
    [InlineData("dòng1\rdòng2")]
    public void BuildCsv_EscapesSpecialCharactersInPlatform(string platform)
    {
        var csv = AnalyticsExportService.BuildCsv([Row(platform)]);

        csv.Should().Contain("\"");
    }

    [Fact]
    public void BuildCsv_PlainPlatform_IsNotQuoted()
    {
        AnalyticsExportService.BuildCsv([Row("zalo")]).Should().NotContain("\"");
    }

    [Fact]
    public void BuildCsv_NullRows_Throws()
    {
        var act = () => AnalyticsExportService.BuildCsv(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildPdf_ProducesPdfHeader()
    {
        var bytes = AnalyticsExportService.BuildPdf([Row(), Row("zalo", null)]);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void BuildPdf_EmptyRows_StillRenders()
    {
        var bytes = AnalyticsExportService.BuildPdf([]);

        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void BuildPdf_NullRows_Throws()
    {
        var act = () => AnalyticsExportService.BuildPdf(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
