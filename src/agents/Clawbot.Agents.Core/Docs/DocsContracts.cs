namespace Clawbot.Agents.Core.Docs;

/// <summary>Input to render one document: a template body + a flat variable bag + branding.</summary>
public sealed record DocsRenderRequest(
    Guid TenantId,
    string TemplateCode,
    string DocType,
    string TemplateBody,
    IReadOnlyDictionary<string, string> Vars,
    DocBranding Branding);

/// <summary>Tenant branding applied to the PDF header/footer.</summary>
public sealed record DocBranding(string TenantName, string? LogoText = null, string? FooterNote = null)
{
    public static DocBranding For(string tenantName) => new(tenantName);
}

/// <summary>Rendered PDF bytes plus integrity (sha256) and timing metadata.</summary>
public sealed record DocsRenderResult(byte[] Pdf, string Sha256, int SizeBytes, long LatencyMs);

/// <summary>Thrown when a template body fails to parse or render.</summary>
public sealed class DocsTemplateException : Exception
{
    public DocsTemplateException(string message) : base(message) { }

    public DocsTemplateException(string message, Exception inner) : base(message, inner) { }
}
