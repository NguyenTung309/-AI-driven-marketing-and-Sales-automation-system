using Clawbot.Agents.Core.Docs;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Docs;

// Engine template mustache-lite + renderer PDF + storage cục bộ (kèm chặn path traversal).
public sealed class DocsServicesTests
{
    // ---------- SimpleTemplateEngine ----------

    [Fact]
    public void Render_SubstitutesKnownPlaceholders()
    {
        var engine = new SimpleTemplateEngine();

        var result = engine.Render("Hello {{ name }}, welcome to {{ product }}.",
            new Dictionary<string, string> { ["name"] = "An", ["product"] = "Học Bá" });

        result.Should().Be("Hello An, welcome to Học Bá.");
    }

    [Fact]
    public void Render_UnknownKey_RendersEmpty()
    {
        var engine = new SimpleTemplateEngine();

        var result = engine.Render("Value=[{{ missing }}]", new Dictionary<string, string>());

        result.Should().Be("Value=[]");
    }

    [Fact]
    public void Render_ToleratesWhitespaceInPlaceholder()
    {
        var engine = new SimpleTemplateEngine();

        var result = engine.Render("{{name}}-{{  name  }}", new Dictionary<string, string> { ["name"] = "x" });

        result.Should().Be("x-x");
    }

    [Fact]
    public void Render_UnbalancedBraces_ThrowsDocsTemplateException()
    {
        var engine = new SimpleTemplateEngine();

        var act = () => engine.Render("Broken {{ name", new Dictionary<string, string> { ["name"] = "x" });

        act.Should().Throw<DocsTemplateException>().WithMessage("*Malformed*");
    }

    [Fact]
    public void Render_NullArguments_Throw()
    {
        var engine = new SimpleTemplateEngine();

        ((Action)(() => engine.Render(null!, new Dictionary<string, string>()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => engine.Render("x", null!))).Should().Throw<ArgumentNullException>();
    }

    // ---------- QuestPdfDocumentRenderer ----------

    [Fact]
    public void PdfRenderer_ProducesNonEmptyPdfBytes()
    {
        var renderer = new QuestPdfDocumentRenderer();

        var pdf = renderer.Render("Báo giá\nDòng nội dung 1\n\nDòng 2", DocBranding.For("Học Bá"), "quote");

        pdf.Should().NotBeNullOrEmpty();
        // Chữ ký magic của file PDF: "%PDF".
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void PdfRenderer_WithQrPayloadAndFooter_StillRenders()
    {
        var renderer = new QuestPdfDocumentRenderer();
        var branding = new DocBranding("Học Bá", LogoText: "HB", FooterNote: "confidential", QrPayload: "https://hocba.vn");

        var pdf = renderer.Render("Title line\nbody", branding, "brochure");

        pdf.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PdfRenderer_NullArguments_Throw()
    {
        var renderer = new QuestPdfDocumentRenderer();

        ((Action)(() => renderer.Render(null!, DocBranding.For("t"), "quote"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => renderer.Render("body", null!, "quote"))).Should().Throw<ArgumentNullException>();
    }

    // ---------- DocBranding ----------

    [Fact]
    public void DocBranding_For_SetsTenantNameOnly()
    {
        var branding = DocBranding.For("Acme");

        branding.TenantName.Should().Be("Acme");
        branding.LogoText.Should().BeNull();
        branding.QrPayload.Should().BeNull();
    }

    // ---------- LocalDocumentStorage ----------

    [Fact]
    public async Task LocalStorage_SaveThenRead_RoundTrips()
    {
        var (storage, dir) = NewStorage();
        try
        {
            var url = await storage.SaveAsync([1, 2, 3], "sub/doc.pdf", "application/pdf");
            url.Should().Be("/generated-docs/sub/doc.pdf");

            var bytes = await storage.ReadAsync("sub/doc.pdf");
            bytes.Should().Equal(1, 2, 3);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task LocalStorage_Delete_RemovesFileIdempotently()
    {
        var (storage, dir) = NewStorage();
        try
        {
            await storage.SaveAsync([9], "d.pdf");
            await storage.DeleteAsync("d.pdf");
            // Xóa lần 2 không được ném dù file đã mất.
            await storage.DeleteAsync("d.pdf");

            var act = async () => await storage.ReadAsync("d.pdf");
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally { Cleanup(dir); }
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("../../etc/passwd")]
    [InlineData("sub/../../escape.pdf")]
    public async Task LocalStorage_PathTraversalKey_Throws(string key)
    {
        var (storage, dir) = NewStorage();
        try
        {
            var act = async () => await storage.SaveAsync([1], key);
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task LocalStorage_BlankFileName_Throws()
    {
        var (storage, dir) = NewStorage();
        try
        {
            var act = async () => await storage.SaveAsync([1], "  ");
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally { Cleanup(dir); }
    }

    private static (LocalDocumentStorage Storage, string Dir) NewStorage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clawbot-docs-test", Guid.NewGuid().ToString("N"));
        var options = new DocsStorageOptions { BaseDirectory = dir, PublicBaseUrl = "/generated-docs" };
        return (new LocalDocumentStorage(options), dir);
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* dọn rác test, bỏ qua nếu OS còn giữ handle */ }
    }
}
