using System.Text;
using Clawbot.Agents.Core.Kb;
using Clawbot.Infrastructure.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace Clawbot.Infrastructure.Tests.Documents;

public sealed class DocumentTextExtractorTests
{
    private readonly DocumentTextExtractor _extractor = new();

    [Theory]
    [InlineData("a.docx", true)]
    [InlineData("a.xlsx", true)]
    [InlineData("a.csv", true)]
    [InlineData("a.PDF", true)]
    [InlineData("a.md", true)]
    [InlineData("a.pptx", false)]
    [InlineData("noext", false)]
    public void CanExtract_MatchesSupportedExtensions(string fileName, bool expected) =>
        _extractor.CanExtract(fileName).Should().Be(expected);

    [Fact]
    public async Task ExtractAsync_Csv_ProducesMarkdownTable()
    {
        var csv = "Khoá,Học phí\nHSK1,2.000.000\nHSK2,2.500.000\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await _extractor.ExtractAsync(stream, "hocphi.csv");

        result.SourceFormat.Should().Be("csv");
        result.Markdown.Should().Contain("| Khoá | Học phí |");
        result.Markdown.Should().Contain("| --- |");
        result.Markdown.Should().Contain("| HSK1 | 2.000.000 |");
    }

    [Fact]
    public async Task ExtractAsync_Csv_EscapesPipeInCell()
    {
        var csv = "Ghi chú\n\"a|b\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = await _extractor.ExtractAsync(stream, "x.csv");

        result.Markdown.Should().Contain("a\\|b");
    }

    [Fact]
    public async Task ExtractAsync_Xlsx_ReadsSharedStringsAndSheetName()
    {
        using var xlsx = BuildXlsx(("Bảng giá", [["Khoá", "Giá"], ["Giao tiếp", "3.000.000"]]));

        var result = await _extractor.ExtractAsync(xlsx, "gia.xlsx");

        result.SourceFormat.Should().Be("xlsx");
        result.Markdown.Should().Contain("## Bảng giá");
        result.Markdown.Should().Contain("| Khoá | Giá |");
        result.Markdown.Should().Contain("| Giao tiếp | 3.000.000 |");
    }

    [Fact]
    public async Task ExtractAsync_Xlsx_MultipleSheets_EmitsHeadingPerSheet()
    {
        using var xlsx = BuildXlsx(
            ("HSK", [["Khoá", "Giá"], ["HSK1", "2tr"]]),
            ("Giao tiếp", [["Khoá", "Giá"], ["GT1", "3tr"]]));

        var result = await _extractor.ExtractAsync(xlsx, "all.xlsx");

        result.Markdown.Should().Contain("## HSK");
        result.Markdown.Should().Contain("## Giao tiếp");
        result.Markdown.Should().Contain("| HSK1 | 2tr |");
        result.Markdown.Should().Contain("| GT1 | 3tr |");
    }

    [Fact]
    public async Task ExtractAsync_Xlsx_ColumnGap_PreservesAlignment()
    {
        // Row 1: A1, B1. Row 2: A2 then C2 (B2 absent) — C2 must land in column 3, not column 2.
        using var xlsx = BuildXlsxWithGap();

        var result = await _extractor.ExtractAsync(xlsx, "gap.xlsx");

        result.Markdown.Should().Contain("| A2 |  | C2 |");
    }

    [Fact]
    public async Task ExtractAsync_DocxRenamedAsXlsx_RejectedByMagicBytes()
    {
        using var docx = BuildDocx(("Normal", "x")); // valid zip, but not a spreadsheet
        // A pure-text file renamed .xlsx must be rejected before OpenXml sees it.
        using var fake = new MemoryStream(Encoding.UTF8.GetBytes("not a real office file"));

        var act = () => _extractor.ExtractAsync(fake, "evil.xlsx");

        await act.Should().ThrowAsync<DocumentExtractionException>();
    }

    [Fact]
    public async Task ExtractAsync_Docx_MapsHeadingStyle()
    {
        using var docx = BuildDocx(("Heading1", "Sĩ số"), ("Normal", "Mỗi lớp tối đa 12 học viên."));

        var result = await _extractor.ExtractAsync(docx, "faq.docx");

        result.SourceFormat.Should().Be("docx");
        result.Markdown.Should().Contain("# Sĩ số");
        result.Markdown.Should().Contain("Mỗi lớp tối đa 12 học viên.");
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedFormat_Throws()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("x"));

        var act = () => _extractor.ExtractAsync(stream, "deck.pptx");

        await act.Should().ThrowAsync<DocumentExtractionException>();
    }

    [Fact]
    public async Task ExtractAsync_EmptyFile_Throws()
    {
        using var stream = new MemoryStream();

        var act = () => _extractor.ExtractAsync(stream, "x.csv");

        await act.Should().ThrowAsync<DocumentExtractionException>();
    }

    // Builds an xlsx using a real SharedStringTable (the common Excel encoding) so the
    // shared-string lookup path in the extractor is exercised, not just inline strings.
    private static MemoryStream BuildXlsx(params (string Name, string[][] Rows)[] sheetSpecs)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, autoSave: true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();

            var sstPart = wbPart.AddNewPart<SharedStringTablePart>();
            sstPart.SharedStringTable = new SharedStringTable();
            var interned = new Dictionary<string, int>(StringComparer.Ordinal);

            int Intern(string value)
            {
                if (interned.TryGetValue(value, out var existing)) return existing;
                var idx = interned.Count;
                sstPart.SharedStringTable.Append(new SharedStringItem(new Text(value)));
                interned[value] = idx;
                return idx;
            }

            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;
            foreach (var (name, rows) in sheetSpecs)
            {
                var wsPart = wbPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                wsPart.Worksheet = new Worksheet(sheetData);
                foreach (var row in rows)
                {
                    var r = new Row();
                    foreach (var cell in row)
                        r.Append(new Cell { DataType = CellValues.SharedString, CellValue = new CellValue(Intern(cell).ToString(System.Globalization.CultureInfo.InvariantCulture)) });
                    sheetData.Append(r);
                }
                sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = sheetId++, Name = name });
            }
        }
        ms.Position = 0;
        return ms;
    }

    // Row 2 deliberately omits B2 and gives C2 an explicit reference, to test column-gap handling.
    private static MemoryStream BuildXlsxWithGap()
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, autoSave: true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            wsPart.Worksheet = new Worksheet(sheetData);

            static Cell Inline(string reference, string text) =>
                new() { CellReference = reference, DataType = CellValues.String, CellValue = new CellValue(text) };

            var row1 = new Row { RowIndex = 1 };
            row1.Append(Inline("A1", "A1"), Inline("B1", "B1"), Inline("C1", "C1"));
            var row2 = new Row { RowIndex = 2 };
            row2.Append(Inline("A2", "A2"), Inline("C2", "C2"));
            sheetData.Append(row1, row2);

            var sheets = wbPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1U, Name = "Gap" });
        }
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream BuildDocx(params (string Style, string Text)[] paragraphs)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, autoSave: true))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Word.Body();
            foreach (var (style, text) in paragraphs)
            {
                var p = new Word.Paragraph(
                    new Word.ParagraphProperties(new Word.ParagraphStyleId { Val = style }),
                    new Word.Run(new Word.Text(text)));
                body.Append(p);
            }
            main.Document = new Word.Document(body);
        }
        ms.Position = 0;
        return ms;
    }
}
