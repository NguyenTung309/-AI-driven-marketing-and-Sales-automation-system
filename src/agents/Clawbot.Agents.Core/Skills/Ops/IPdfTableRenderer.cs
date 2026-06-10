using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record PdfTableSpec(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? Title,
    IReadOnlyDictionary<string, string>? Style);

public sealed record PdfRenderResult(byte[] PdfBytes, string MimeType);

public interface IPdfTableRenderer : ISkill
{
    Task<PdfRenderResult> RenderAsync(PdfTableSpec spec, CancellationToken ct);
}

internal sealed class QuestPdfTableRenderer : IPdfTableRenderer
{
    public string Name => "pdf-table-renderer";

    public Task<PdfRenderResult> RenderAsync(PdfTableSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Headers.Count == 0)
            throw new ArgumentException("Headers must not be empty.", nameof(spec));

        var fontSize = spec.Style?.TryGetValue("font_size", out var fs) == true && int.TryParse(fs, out var sz) ? sz : 10;
        var headerBg = Colors.Grey.Lighten2;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(30);
                page.MarginVertical(25);

                if (!string.IsNullOrWhiteSpace(spec.Title))
                {
                    page.Header().Text(spec.Title).FontSize(16).SemiBold().FontColor(Colors.Blue.Darken2);
                }

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        foreach (var _ in spec.Headers)
                            cols.RelativeColumn();
                    });

                    table.Header(hdr =>
                    {
                        foreach (var h in spec.Headers)
                        {
                            hdr.Cell()
                                .Background(headerBg)
                                .Padding(5)
                                .Text(h)
                                .FontSize(fontSize)
                                .SemiBold();
                        }
                    });

                    foreach (var row in spec.Rows)
                    {
                        for (var i = 0; i < spec.Headers.Count; i++)
                        {
                            var cellText = i < row.Count ? row[i] : string.Empty;
                            table.Cell()
                                .BorderBottom(1)
                                .BorderColor(Colors.Grey.Lighten1)
                                .Padding(4)
                                .Text(cellText)
                                .FontSize(fontSize);
                        }
                    }
                });

                page.Footer()
                    .AlignCenter()
                    .Text(txt =>
                    {
                        txt.CurrentPageNumber();
                        txt.Span(" / ");
                        txt.TotalPages();
                    });
            });
        }).GeneratePdf();

        return Task.FromResult(new PdfRenderResult(pdfBytes, "application/pdf"));
    }
}
