using Clawbot.Api.Contracts.Analytics;
using Clawbot.Api.Services;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class AnalyticsExportTests
{
    [Fact]
    public void BuildCsv_writes_stable_header_and_rows()
    {
        var rows = new[]
        {
            new OmniChannelRowDto("facebook", 12, 5, 4, 2, 123.45m, 67.89m, 5.66m),
            new OmniChannelRowDto("youtube", 3, 1, 0, 0, null, null, null),
        };

        var csv = AnalyticsExportService.BuildCsv(rows);

        csv.Should().Be(
            "platform,leads,dms,replies,conversions,avg_response_time_sec,ad_spend,cpl\r\n" +
            "facebook,12,5,4,2,123.45,67.89,5.66\r\n" +
            "youtube,3,1,0,0,,,\r\n");
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("pdf")]
    [InlineData("CSV")]
    public void NormalizeFormat_accepts_supported_formats(string value)
    {
        AnalyticsExportService.NormalizeFormat(value).Should().Be(value.ToLowerInvariant());
    }

    [Fact]
    public void NormalizeFormat_rejects_unsupported_formats()
    {
        var act = () => AnalyticsExportService.NormalizeFormat("xlsx");

        act.Should().Throw<ArgumentException>().WithMessage("Unsupported export format*");
    }

    [Fact]
    public void BuildPdf_returns_generated_pdf_document()
    {
        var rows = new[]
        {
            new OmniChannelRowDto("facebook", 12, 5, 4, 2, 123.45m, 67.89m, 5.66m),
            new OmniChannelRowDto("youtube", 3, 1, 0, 0, null, null, null),
        };

        var pdf = AnalyticsExportService.BuildPdf(rows);

        pdf.Should().StartWith("%PDF"u8.ToArray());
        pdf.Length.Should().BeGreaterThan(1500);
    }
}
