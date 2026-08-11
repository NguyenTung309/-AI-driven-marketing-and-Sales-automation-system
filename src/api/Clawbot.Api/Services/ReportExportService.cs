using System.Globalization;
using System.Text;
using System.Text.Json;
using Clawbot.Domain.Analytics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// OpenXml.Spreadsheet cũng có Colors/Fonts: alias để nhánh PDF khỏi bốc nhầm type của Excel.
using QColors = QuestPDF.Helpers.Colors;
using QFonts = QuestPDF.Helpers.Fonts;

namespace Clawbot.Api.Services;

/// <summary>
/// Xuất artifact báo cáo ra csv/xlsx/pdf. Bảng là động (cột do payload quyết định) nên mọi định dạng
/// đều duyệt theo <see cref="ReportArtifactPayload.Columns"/> thay vì hard-code cột như export KPI cũ.
/// </summary>
public sealed class ReportExportService
{
    static ReportExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static readonly IReadOnlyList<string> SupportedFormats = ["csv", "xlsx", "pdf"];

    public static string NormalizeFormat(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant();
        if (!SupportedFormats.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentException(string.Create(
                CultureInfo.InvariantCulture,
                $"Unsupported format '{format}'. Supported: {string.Join(", ", SupportedFormats)}."));
        }

        return normalized;
    }

    public static string ContentTypeFor(string format) => format switch
    {
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "pdf" => "application/pdf",
        _ => "text/csv",
    };

    public static byte[] Build(string format, string title, ReportArtifactPayload payload) => format switch
    {
        "xlsx" => BuildXlsx(payload),
        "pdf" => BuildPdf(title, payload),
        // BOM: Excel bản Windows đọc CSV theo ANSI nếu thiếu nó, tiêu đề cột tiếng Việt sẽ vỡ hết dấu.
        _ => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(BuildCsv(payload))).ToArray(),
    };

    public static string BuildCsv(ReportArtifactPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var sb = new StringBuilder();
        sb.AppendJoin(',', payload.Columns.Select(c => Escape(c.Label))).Append("\r\n");
        foreach (var row in payload.Rows)
        {
            sb.AppendJoin(',', payload.Columns.Select(c => Escape(Text(Cell(row, c.Key))))).Append("\r\n");
        }

        return sb.ToString();
    }

    public static byte[] BuildXlsx(ReportArtifactPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sheetData = new SheetData();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Report",
            });

            var header = new Row();
            foreach (var column in payload.Columns)
                header.Append(TextCell(column.Label));
            sheetData.Append(header);

            foreach (var row in payload.Rows)
            {
                var sheetRow = new Row();
                foreach (var column in payload.Columns)
                {
                    var value = Cell(row, column.Key);
                    var number = Number(value);
                    sheetRow.Append(number.HasValue ? NumberCell(number.Value) : TextCell(Text(value)));
                }

                sheetData.Append(sheetRow);
            }

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    public static byte[] BuildPdf(string title, ReportArtifactPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(QFonts.Calibri));

                page.Header().Text(title)
                    .FontSize(15)
                    .SemiBold()
                    .FontColor(QColors.Blue.Darken2);

                page.Content().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var column in payload.Columns)
                            columns.RelativeColumn(column.Type == "text" ? 1.4f : 1f);
                    });

                    table.Header(header =>
                    {
                        foreach (var column in payload.Columns)
                            header.Cell().Element(HeaderCell).Text(column.Label);
                    });

                    foreach (var row in payload.Rows)
                    {
                        foreach (var column in payload.Columns)
                            table.Cell().Element(BodyCell).Text(Text(Cell(row, column.Key)));
                    }
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(QColors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(4).DefaultTextStyle(t => t.SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(QColors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(4);

    private static object? Cell(IReadOnlyDictionary<string, object?> row, string key) =>
        row is not null && row.TryGetValue(key, out var value) ? value : null;

    // Giá trị đi ra từ DataJson nên luôn là JsonElement; các nhánh còn lại chỉ để gọi trực tiếp không nổ.
    private static double? Number(object? value) => value switch
    {
        JsonElement el => el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) ? d : null,
        double d => d,
        float f => f,
        decimal m => (double)m,
        int i => i,
        long l => l,
        _ => null,
    };

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        JsonElement el => el.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.GetRawText(),
        },
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value) =>
        value.Contains(',', StringComparison.Ordinal)
        || value.Contains('"', StringComparison.Ordinal)
        || value.Contains('\n', StringComparison.Ordinal)
            ? string.Create(CultureInfo.InvariantCulture, $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"")
            : value;

    private static Cell TextCell(string value) => new()
    {
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value)),
    };

    private static Cell NumberCell(double value) => new()
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
    };
}
