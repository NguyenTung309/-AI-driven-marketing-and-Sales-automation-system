using System.Security.Cryptography;
using System.Text;
using Clawbot.Agents.Core.Docs;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Docs;

// M17 — Document Generation: template resolution, PDF rendering, agent orchestration, local storage.
public sealed class SimpleTemplateEngineTests
{
    private readonly SimpleTemplateEngine _engine = new();

    [Fact]
    public void Substitutes_known_variables()
    {
        var output = _engine.Render("Xin chào {{ name }}, gói {{ plan }}.",
            new Dictionary<string, string> { ["name"] = "An", ["plan"] = "HSK3" });

        output.Should().Be("Xin chào An, gói HSK3.");
    }

    [Fact]
    public void Unknown_variable_renders_empty_without_throwing()
    {
        var output = _engine.Render("Giá: {{ missing }}đ", new Dictionary<string, string>());

        output.Should().Be("Giá: đ");
    }

    [Fact]
    public void Invalid_template_throws_DocsTemplateException()
    {
        var act = () => _engine.Render("{{ if x }}{{ end", new Dictionary<string, string>());

        act.Should().Throw<DocsTemplateException>();
    }

    [Fact]
    public void Null_args_throw()
    {
        ((Action)(() => _engine.Render(null!, new Dictionary<string, string>())))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => _engine.Render("x", null!)))
            .Should().Throw<ArgumentNullException>();
    }
}

public sealed class QuestPdfDocumentRendererTests
{
    private readonly QuestPdfDocumentRenderer _renderer = new();

    [Fact]
    public void Produces_pdf_with_magic_header()
    {
        var pdf = _renderer.Render("Dòng một.\n\nDòng hai.", new DocBranding("Trung tâm Hoa Ngữ"), "quote");

        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(500);
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Null_body_throws()
    {
        ((Action)(() => _renderer.Render(null!, new DocBranding("X"), "quote")))
            .Should().Throw<ArgumentNullException>();
    }
}

public sealed class DocsAgentTests
{
    private static DocsAgent Build() => new(new SimpleTemplateEngine(), new QuestPdfDocumentRenderer());

    private static DocsRenderRequest Request(string body, IReadOnlyDictionary<string, string>? vars = null) =>
        new(Guid.NewGuid(), "QUOTE-V1", "quote", body,
            vars ?? new Dictionary<string, string>(), new DocBranding("Trung tâm Hoa Ngữ"));

    [Fact]
    public async Task Render_resolves_template_and_returns_consistent_hash()
    {
        var agent = Build();

        var result = await agent.RenderAsync(
            Request("Báo giá cho {{ name }}.", new Dictionary<string, string> { ["name"] = "An" }),
            CancellationToken.None);

        Encoding.ASCII.GetString(result.Pdf, 0, 5).Should().Be("%PDF-");
        result.SizeBytes.Should().Be(result.Pdf.Length);
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);

        var expectedHash = Convert.ToHexString(SHA256.HashData(result.Pdf)).ToLowerInvariant();
        result.Sha256.Should().Be(expectedHash);
    }

    [Fact]
    public async Task Empty_template_body_throws_DocsTemplateException()
    {
        var agent = Build();

        var act = async () => await agent.RenderAsync(Request("   "), CancellationToken.None);

        await act.Should().ThrowAsync<DocsTemplateException>();
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var agent = Build();

        var act = async () => await agent.RenderAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Null_dependencies_throw()
    {
        ((Action)(() => _ = new DocsAgent(null!, new QuestPdfDocumentRenderer())))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new DocsAgent(new SimpleTemplateEngine(), null!)))
            .Should().Throw<ArgumentNullException>();
    }
}

public sealed class LocalDocumentStorageTests
{
    [Fact]
    public async Task Saves_bytes_and_returns_public_url()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clawbot-docs-test-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalDocumentStorage(new DocsStorageOptions
        {
            BaseDirectory = dir,
            PublicBaseUrl = "https://cdn.example.com/docs/",
        });
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 test");

        try
        {
            var url = await storage.SaveAsync(bytes, "quote-v1.pdf", ct: CancellationToken.None);

            url.Should().Be("https://cdn.example.com/docs/quote-v1.pdf");
            File.Exists(Path.Combine(dir, "quote-v1.pdf")).Should().BeTrue();
            (await File.ReadAllBytesAsync(Path.Combine(dir, "quote-v1.pdf"))).Should().Equal(bytes);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Blank_filename_throws()
    {
        var storage = new LocalDocumentStorage(new DocsStorageOptions());

        var act = async () => await storage.SaveAsync([1, 2, 3], "  ", ct: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
