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

// Source: https://www.questpdf.com/  (Community license — free for orgs < $1M revenue).
internal sealed class QuestPdfTableRenderer : IPdfTableRenderer
{
    public string Name => "pdf-table-renderer";

    public Task<PdfRenderResult> RenderAsync(PdfTableSpec spec, CancellationToken ct) =>
        throw new NotImplementedException("TODO: QuestPDF Document.Create(...).GeneratePdf().");
}
