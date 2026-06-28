using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Clawbot.Agents.Core.Kb;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace Clawbot.Infrastructure.Documents;

/// <summary>
/// File → markdown for KB ingestion. docx/xlsx use OpenXml, pdf uses PdfPig text extraction,
/// csv/txt/md pass through with light formatting. Output is always a DRAFT the operator edits
/// before deploy, so structure-loss (esp. in pdf) is acceptable — we surface it, not hide it.
/// </summary>
public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly string[] Extensions = [".docx", ".xlsx", ".csv", ".pdf", ".txt", ".md"];

    // Hard cap on the *actual* bytes read, independent of the client-reported Content-Length.
    private const long MaxBytes = 10 * 1024 * 1024;

    private static readonly Regex CollapseBlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public bool CanExtract(string fileName) =>
        Extensions.Contains(Ext(fileName), StringComparer.OrdinalIgnoreCase);

    public async Task<ExtractedDocument> ExtractAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ext = Ext(fileName);

        // Buffer to a seekable copy: OpenXml/PdfPig need random access; upload streams may not seek.
        // Enforce the byte cap on the real stream — IFormFile.Length is the spoofable Content-Length header.
        using var buffer = new MemoryStream();
        await CopyCappedAsync(content, buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;
        if (buffer.Length == 0)
            throw new DocumentExtractionException("Tệp rỗng.");

        VerifyMagicBytes(ext, buffer);
        buffer.Position = 0;

        var markdown = ext switch
        {
            ".docx" => ExtractDocx(buffer),
            ".xlsx" => ExtractXlsx(buffer),
            ".csv" => ExtractCsv(ReadAllText(buffer)),
            ".pdf" => ExtractPdf(buffer),
            ".txt" => ReadAllText(buffer).Trim(),
            ".md" => ReadAllText(buffer).Trim(),
            _ => throw new DocumentExtractionException($"Định dạng không hỗ trợ: {ext}. Hỗ trợ: {string.Join(", ", Extensions)}."),
        };

        markdown = Normalize(markdown);
        if (string.IsNullOrWhiteSpace(markdown))
            throw new DocumentExtractionException("Không trích xuất được nội dung văn bản từ tệp.");

        return new ExtractedDocument(markdown, markdown.Length, ext.TrimStart('.'));
    }

    private static string Ext(string fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

    // Copies up to MaxBytes; throws if the source has more. Guards against a tiny declared
    // Content-Length hiding a huge stream, and against decompression-bomb input pre-parse.
    private static async Task CopyCappedAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxBytes)
                throw new DocumentExtractionException("Tệp vượt quá giới hạn 10MB.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    // Filename extension alone drives parser dispatch; confirm the bytes match before handing
    // an attacker-renamed payload to OpenXml/PdfPig. Text formats (csv/txt/md) need no check.
    private static void VerifyMagicBytes(string ext, MemoryStream buffer)
    {
        ReadOnlySpan<byte> head = buffer.GetBuffer().AsSpan(0, (int)Math.Min(4, buffer.Length));
        switch (ext)
        {
            case ".docx" or ".xlsx":
                // OOXML is a ZIP container: "PK\x03\x04" (or empty-archive "PK\x05\x06").
                if (!(head.Length >= 2 && head[0] == 0x50 && head[1] == 0x4B))
                    throw new DocumentExtractionException("Nội dung tệp không khớp định dạng Office.");
                break;
            case ".pdf":
                if (!(head.Length >= 4 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46))
                    throw new DocumentExtractionException("Nội dung tệp không phải PDF hợp lệ.");
                break;
        }
    }

    private static string ReadAllText(Stream s)
    {
        s.Position = 0;
        using var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    // ---- docx -------------------------------------------------------------
    private static string ExtractDocx(Stream s)
    {
        s.Position = 0;
        using var doc = WordprocessingDocument.Open(s, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Word.Paragraph p:
                    AppendParagraph(sb, p);
                    break;
                case Word.Table t:
                    AppendDocxTable(sb, t);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void AppendParagraph(StringBuilder sb, Word.Paragraph p)
    {
        var text = p.InnerText?.Trim() ?? string.Empty;
        if (text.Length == 0) return;

        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        var heading = HeadingLevel(styleId);
        if (heading > 0)
            sb.Append('#', heading).Append(' ').AppendLine(text);
        else
            sb.AppendLine(text);
        sb.AppendLine();
    }

    private static int HeadingLevel(string styleId)
    {
        // OpenXml heading styles are "Heading1".."Heading9" (or "Title").
        if (styleId.StartsWith("Title", StringComparison.OrdinalIgnoreCase)) return 1;
        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(styleId.AsSpan("Heading".Length), out var n) && n is >= 1 and <= 6)
            return n;
        return 0;
    }

    private static void AppendDocxTable(StringBuilder sb, Word.Table table)
    {
        var rows = table.Elements<Word.TableRow>()
            .Select(r => r.Elements<Word.TableCell>().Select(c => CleanCell(c.InnerText)).ToList())
            .Where(cells => cells.Count > 0)
            .ToList();
        AppendMarkdownTable(sb, rows);
    }

    // ---- xlsx (the real pricing/FAQ source) -------------------------------
    private static string ExtractXlsx(Stream s)
    {
        s.Position = 0;
        using var doc = SpreadsheetDocument.Open(s, isEditable: false);
        var workbookPart = doc.WorkbookPart;
        var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList();
        if (workbookPart is null || sheets is null || sheets.Count == 0) return string.Empty;

        // Materialize the shared-string table once (O(1) indexed lookup instead of O(N) per cell).
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
            ?.Elements<SharedStringItem>().Select(si => si.InnerText).ToList();
        var sb = new StringBuilder();
        foreach (var sheet in sheets)
        {
            if (sheet.Id?.Value is not { } relId) continue;
            if (workbookPart.GetPartById(relId) is not WorksheetPart wsPart) continue;

            sb.Append("## ").AppendLine(sheet.Name?.Value ?? "Sheet");
            sb.AppendLine();

            var rows = wsPart.Worksheet.Descendants<Row>()
                .Select(r => ReadRow(r, sharedStrings))
                .Where(cells => cells.Any(c => c.Length > 0))
                .ToList();
            AppendMarkdownTable(sb, rows);
        }
        return sb.ToString();
    }

    private static List<string> ReadRow(Row row, IReadOnlyList<string>? sharedStrings)
    {
        // Honor column position so blank cells don't shift the table left.
        var cells = new List<string>();
        var expected = 1;
        foreach (var cell in row.Elements<Cell>())
        {
            // A cell with no reference sits at the next sequential position, not column 1.
            var col = ColumnIndex(cell.CellReference?.Value, expected);
            while (col > expected) { cells.Add(string.Empty); expected++; }
            cells.Add(CleanCell(ReadCell(cell, sharedStrings)));
            expected++;
        }
        return cells;
    }

    private static string ReadCell(Cell cell, IReadOnlyList<string>? sharedStrings)
    {
        var raw = cell.CellValue?.InnerText ?? cell.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
            && sharedStrings is not null && idx >= 0 && idx < sharedStrings.Count)
        {
            return sharedStrings[idx];
        }
        return raw;
    }

    private static int ColumnIndex(string? cellRef, int fallback)
    {
        if (string.IsNullOrEmpty(cellRef)) return fallback;
        var col = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return col <= 0 ? fallback : col;
    }

    // ---- csv --------------------------------------------------------------
    private static string ExtractCsv(string text)
    {
        var rows = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => line.Trim().Length > 0)
            .Select(line => ParseCsvLine(line).Select(CleanCell).ToList())
            .ToList();
        var sb = new StringBuilder();
        AppendMarkdownTable(sb, rows);
        return sb.ToString();
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(ch);
        }
        fields.Add(field.ToString());
        return fields;
    }

    // ---- pdf (lossy; operator edits the draft) ----------------------------
    private static string ExtractPdf(Stream s)
    {
        s.Position = 0;
        using var pdf = PdfDocument.Open(s);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            var text = page.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            sb.AppendLine(text);
            sb.AppendLine();
        }
        var result = sb.ToString();
        if (string.IsNullOrWhiteSpace(result))
            throw new DocumentExtractionException(
                "PDF không có lớp văn bản (có thể là bản scan/ảnh). Hãy dùng nguồn docx/xlsx hoặc bật OCR trước.");
        return result;
    }

    // ---- shared -----------------------------------------------------------
    private static string CleanCell(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static void AppendMarkdownTable(StringBuilder sb, List<List<string>> rows)
    {
        if (rows.Count == 0) return;
        var width = rows.Max(r => r.Count);
        if (width == 0) return;

        AppendRow(sb, rows[0], width);
        sb.Append('|').AppendLine(string.Concat(Enumerable.Repeat(" --- |", width)));
        foreach (var row in rows.Skip(1))
            AppendRow(sb, row, width);
        sb.AppendLine();
    }

    private static void AppendRow(StringBuilder sb, List<string> cells, int width)
    {
        sb.Append("| ");
        for (var i = 0; i < width; i++)
        {
            sb.Append(i < cells.Count ? cells[i] : string.Empty);
            sb.Append(" | ");
        }
        sb.Length -= 1; // drop trailing space
        sb.AppendLine();
    }

    private static string Normalize(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        // Collapse 3+ newlines to one blank line in a single pass (KB chunker splits on blank lines).
        return CollapseBlankLines.Replace(normalized, "\n\n").Trim();
    }
}
