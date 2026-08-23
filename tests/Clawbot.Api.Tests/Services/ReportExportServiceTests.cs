using System.Text;
using System.Text.Json;
using Clawbot.Api.Services;
using Clawbot.Domain.Analytics;
using FluentAssertions;

namespace Clawbot.Api.Tests.Services;

public sealed class ReportExportServiceTests
{
    private static ReportArtifactPayload Payload(
        IReadOnlyList<ReportColumn>? columns = null,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null) =>
        new(
            "snapshot",
            columns ??
            [
                new ReportColumn("platform", "Nền tảng", "text"),
                new ReportColumn("leads", "Số lead", "number"),
            ],
            rows ??
            [
                new Dictionary<string, object?> { ["platform"] = "facebook", ["leads"] = 12 },
                new Dictionary<string, object?> { ["platform"] = "zalo", ["leads"] = 7 },
            ],
            Chart: null);

    private static Dictionary<string, object?> JsonRow(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

    [Theory]
    [InlineData(null, "csv")]
    [InlineData("", "csv")]
    [InlineData("   ", "csv")]
    [InlineData("CSV", "csv")]
    [InlineData("  XLSX  ", "xlsx")]
    [InlineData("pdf", "pdf")]
    public void NormalizeFormat_ValidInput_LowercasesAndDefaults(string? input, string expected)
    {
        ReportExportService.NormalizeFormat(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("json")]
    public void NormalizeFormat_UnsupportedFormat_Throws(string input)
    {
        var act = () => ReportExportService.NormalizeFormat(input);

        act.Should().Throw<ArgumentException>().WithMessage("*Unsupported format*");
    }

    [Fact]
    public void SupportedFormats_AreCsvXlsxPdf()
    {
        ReportExportService.SupportedFormats.Should().Equal("csv", "xlsx", "pdf");
    }

    [Theory]
    [InlineData("csv", "text/csv")]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("pdf", "application/pdf")]
    [InlineData("unknown", "text/csv")]
    public void ContentTypeFor_MapsFormatToMimeType(string format, string expected)
    {
        ReportExportService.ContentTypeFor(format).Should().Be(expected);
    }

    [Fact]
    public void BuildCsv_WritesHeaderAndRowsWithCrLf()
    {
        var csv = ReportExportService.BuildCsv(Payload());

        csv.Should().Be("Nền tảng,Số lead\r\nfacebook,12\r\nzalo,7\r\n");
    }

    [Fact]
    public void BuildCsv_EscapesCommaQuoteAndNewline()
    {
        var payload = Payload(
            [new ReportColumn("note", "Ghi chú", "text")],
            [
                new Dictionary<string, object?> { ["note"] = "a,b" },
                new Dictionary<string, object?> { ["note"] = "nói \"xin chào\"" },
                new Dictionary<string, object?> { ["note"] = "dòng1\ndòng2" },
            ]);

        var csv = ReportExportService.BuildCsv(payload);

        csv.Should().Contain("\"a,b\"");
        csv.Should().Contain("\"nói \"\"xin chào\"\"\"");
        csv.Should().Contain("\"dòng1\ndòng2\"");
    }

    [Fact]
    public void BuildCsv_MissingKey_EmitsEmptyCell()
    {
        var payload = Payload(
            [
                new ReportColumn("platform", "Nền tảng", "text"),
                new ReportColumn("absent", "Thiếu", "text"),
            ],
            [new Dictionary<string, object?> { ["platform"] = "facebook" }]);

        ReportExportService.BuildCsv(payload).Should().Be("Nền tảng,Thiếu\r\nfacebook,\r\n");
    }

    [Fact]
    public void BuildCsv_RendersJsonElementValuesByKind()
    {
        var payload = Payload(
            [
                new ReportColumn("s", "S", "text"),
                new ReportColumn("n", "N", "number"),
                new ReportColumn("t", "T", "text"),
                new ReportColumn("f", "F", "text"),
                new ReportColumn("z", "Z", "text"),
                new ReportColumn("arr", "Arr", "text"),
            ],
            [JsonRow("""{"s":"chuỗi","n":12.5,"t":true,"f":false,"z":null,"arr":[1,2]}""")]);

        var csv = ReportExportService.BuildCsv(payload);

        csv.Should().Contain("chuỗi");
        csv.Should().Contain("12.5");
        csv.Should().Contain("true");
        csv.Should().Contain("false");
        csv.Should().Contain("\"[1,2]\"");
    }

    [Fact]
    public void BuildCsv_NullPayload_Throws()
    {
        var act = () => ReportExportService.BuildCsv(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCsv_NoRows_EmitsHeaderOnly()
    {
        var payload = Payload(rows: []);

        ReportExportService.BuildCsv(payload).Should().Be("Nền tảng,Số lead\r\n");
    }

    [Fact]
    public void Build_Csv_PrefixesUtf8BomSoExcelKeepsVietnameseDiacritics()
    {
        var bytes = ReportExportService.Build("csv", "Báo cáo", Payload());

        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(bytes).Should().Contain("Nền tảng");
    }

    [Fact]
    public void Build_UnknownFormat_FallsBackToCsv()
    {
        var bytes = ReportExportService.Build("unknown", "Báo cáo", Payload());

        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
    }

    [Fact]
    public void Build_Xlsx_ProducesZipContainer()
    {
        var bytes = ReportExportService.Build("xlsx", "Báo cáo", Payload());

        // OOXML là zip: 2 byte đầu luôn là "PK".
        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');
    }

    [Fact]
    public void BuildXlsx_NumericValuesUseNumberCells()
    {
        var payload = Payload(
            [new ReportColumn("n", "N", "number")],
            [
                JsonRow("""{"n":42}"""),
                new Dictionary<string, object?> { ["n"] = 3.5d },
                new Dictionary<string, object?> { ["n"] = 7f },
                new Dictionary<string, object?> { ["n"] = 9.25m },
                new Dictionary<string, object?> { ["n"] = 11 },
                new Dictionary<string, object?> { ["n"] = 13L },
                new Dictionary<string, object?> { ["n"] = "không phải số" },
            ]);

        var bytes = ReportExportService.BuildXlsx(payload);

        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildXlsx_NullPayload_Throws()
    {
        var act = () => ReportExportService.BuildXlsx(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_Pdf_ProducesPdfHeader()
    {
        var bytes = ReportExportService.Build("pdf", "Báo cáo tháng 8", Payload());

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void BuildPdf_NullPayload_Throws()
    {
        var act = () => ReportExportService.BuildPdf("t", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildPdf_EmptyRows_StillRenders()
    {
        var bytes = ReportExportService.BuildPdf("Rỗng", Payload(rows: []));

        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }
}
