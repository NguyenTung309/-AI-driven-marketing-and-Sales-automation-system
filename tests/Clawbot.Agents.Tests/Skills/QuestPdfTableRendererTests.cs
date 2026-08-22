using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using QuestPDF.Infrastructure;

namespace Clawbot.Agents.Tests.Skills;

// Render bảng PDF bằng QuestPDF: cần header, sinh magic bytes %PDF, ô thiếu điền rỗng.
public sealed class QuestPdfTableRendererTests
{
    static QuestPdfTableRendererTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static QuestPdfTableRenderer NewRenderer() => new();

    private static PdfTableSpec Spec(
        IReadOnlyList<string>? headers = null,
        IReadOnlyList<IReadOnlyList<string>>? rows = null,
        string? title = null,
        IReadOnlyDictionary<string, string>? style = null)
        => new(
            headers ?? new[] { "Cột A", "Cột B" },
            rows ?? new IReadOnlyList<string>[] { new[] { "1", "2" } },
            title,
            style);

    [Fact]
    public void Name_IsPdfTableRenderer()
    {
        NewRenderer().Name.Should().Be("pdf-table-renderer");
    }

    [Fact]
    public async Task Render_NullSpec_Throws()
    {
        var act = async () => await NewRenderer().RenderAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Render_EmptyHeaders_Throws()
    {
        var act = async () => await NewRenderer().RenderAsync(Spec(headers: Array.Empty<string>()), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Render_ValidSpec_ProducesPdfBytes()
    {
        var result = await NewRenderer().RenderAsync(Spec(title: "Báo cáo"), CancellationToken.None);

        result.MimeType.Should().Be("application/pdf");
        result.PdfBytes.Should().NotBeEmpty();
        // Magic header %PDF.
        System.Text.Encoding.ASCII.GetString(result.PdfBytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Render_RowShorterThanHeaders_PadsMissingCells()
    {
        var spec = Spec(
            headers: new[] { "A", "B", "C" },
            rows: new IReadOnlyList<string>[] { new[] { "only-one" } });

        var result = await NewRenderer().RenderAsync(spec, CancellationToken.None);

        result.PdfBytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Render_CustomFontSizeStyle_Honored()
    {
        var spec = Spec(style: new Dictionary<string, string> { ["font_size"] = "14" });

        var result = await NewRenderer().RenderAsync(spec, CancellationToken.None);

        result.PdfBytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Render_NoTitle_StillRenders()
    {
        var result = await NewRenderer().RenderAsync(Spec(title: null), CancellationToken.None);

        result.PdfBytes.Should().NotBeEmpty();
    }
}
