using System.Globalization;
using System.Text.RegularExpressions;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Clawbot.Agents.Core.Docs;

/// <summary>Resolves a text template against a flat string variable bag.</summary>
public interface ITemplateEngine
{
    string Render(string templateBody, IReadOnlyDictionary<string, string> vars);
}

/// <summary>
/// Mustache-lite engine: substitutes <c>{{ key }}</c> placeholders. Unknown keys render empty;
/// leftover unbalanced braces are treated as a malformed template. No external dependency —
/// deliberately not a full templating engine (the document use-case is field substitution only).
/// </summary>
public sealed partial class SimpleTemplateEngine : ITemplateEngine
{
    [GeneratedRegex(@"\{\{\s*(?<key>[^{}]*?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    public string Render(string templateBody, IReadOnlyDictionary<string, string> vars)
    {
        ArgumentNullException.ThrowIfNull(templateBody);
        ArgumentNullException.ThrowIfNull(vars);

        var result = PlaceholderRegex().Replace(templateBody, match =>
        {
            var key = match.Groups["key"].Value;
            return vars.TryGetValue(key, out var value) ? value : string.Empty;
        });

        if (result.Contains("{{", StringComparison.Ordinal) || result.Contains("}}", StringComparison.Ordinal))
            throw new DocsTemplateException("Malformed template: unbalanced '{{' or '}}'.");

        return result;
    }
}

/// <summary>Renders resolved document text into a branded PDF byte array.</summary>
public interface IDocumentRenderer
{
    byte[] Render(string resolvedBody, DocBranding branding, string docType);
}

/// <summary>QuestPDF A4 renderer: branded header, paragraph body, page-number footer.</summary>
public sealed class QuestPdfDocumentRenderer : IDocumentRenderer
{
    static QuestPdfDocumentRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(string resolvedBody, DocBranding branding, string docType)
    {
        ArgumentNullException.ThrowIfNull(resolvedBody);
        ArgumentNullException.ThrowIfNull(branding);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily(Fonts.Calibri));

                page.Header().Element(h => ComposeHeader(h, branding, docType));
                page.Content().Element(c => ComposeBody(c, resolvedBody));
                page.Footer().Element(f => ComposeFooter(f, branding));
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, DocBranding branding, string docType)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(branding.LogoText ?? branding.TenantName)
                    .FontSize(16).Bold().FontColor(Colors.Blue.Darken2);
                col.Item().Text(branding.TenantName).FontSize(9).FontColor(Colors.Grey.Darken1);
            });
            row.ConstantItem(150).AlignRight().Text(DocTypeLabel(docType))
                .FontSize(12).SemiBold().FontColor(Colors.Grey.Darken3);
        });
    }

    private static void ComposeBody(IContainer container, string resolvedBody)
    {
        container.PaddingVertical(12).Column(col =>
        {
            col.Spacing(8);
            var normalized = resolvedBody.Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (var paragraph in normalized.Split('\n'))
            {
                var text = paragraph.TrimEnd();
                if (text.Length == 0)
                {
                    col.Item().Height(4);
                    continue;
                }

                col.Item().Text(text);
            }
        });
    }

    private static void ComposeFooter(IContainer container, DocBranding branding)
    {
        container.BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(branding.FooterNote ?? string.Empty)
                .FontSize(8).FontColor(Colors.Grey.Darken1);
            if (!string.IsNullOrWhiteSpace(branding.QrPayload))
                row.ConstantItem(40).AlignRight().Height(40).Image(QrPng(branding.QrPayload));
            row.ConstantItem(90).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                t.Span("Trang ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }

    // QR footer (M17): same QRCoder lib as IQrGenerator, called sync from the renderer.
    private static byte[] QrPng(string payload)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(4);
    }

    private static string DocTypeLabel(string? docType) => (docType ?? string.Empty).ToUpperInvariant() switch
    {
        "QUOTE" => "BÁO GIÁ",
        "BROCHURE" => "BROCHURE",
        "SLIDE" => "SLIDE",
        "ONBOARDING" => "ONBOARDING",
        _ => "TÀI LIỆU",
    };
}

/// <summary>Persists a rendered document and returns a retrievable URL.</summary>
public interface IDocumentStorage
{
    Task<string> SaveAsync(byte[] content, string fileName, CancellationToken ct = default);
}

/// <summary>Options for <see cref="LocalDocumentStorage"/>. Bound from config section <c>Docs:Storage</c>.</summary>
public sealed class DocsStorageOptions
{
    public const string SectionName = "Docs:Storage";

    public string BaseDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "generated-docs");

    public string PublicBaseUrl { get; set; } = "/generated-docs";
}

/// <summary>Local-disk storage. Writes under BaseDirectory, returns a PublicBaseUrl-rooted URL. MinIO swap deferred.</summary>
public sealed class LocalDocumentStorage : IDocumentStorage
{
    private readonly DocsStorageOptions _options;

    public LocalDocumentStorage(DocsStorageOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<string> SaveAsync(byte[] content, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName required", nameof(fileName));

        Directory.CreateDirectory(_options.BaseDirectory);
        var safeName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(_options.BaseDirectory, safeName);
        await File.WriteAllBytesAsync(fullPath, content, ct).ConfigureAwait(false);

        var baseUrl = _options.PublicBaseUrl.TrimEnd('/');
        return string.Create(CultureInfo.InvariantCulture, $"{baseUrl}/{safeName}");
    }
}
