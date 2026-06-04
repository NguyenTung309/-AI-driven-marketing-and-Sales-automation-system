using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Docs;

/// <summary>Pure document orchestrator: resolve template variables, render PDF, hash. No DB or IO.</summary>
public sealed class DocsAgent
{
    private readonly ITemplateEngine _templates;
    private readonly IDocumentRenderer _renderer;

    public DocsAgent(ITemplateEngine templates, IDocumentRenderer renderer)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public Task<DocsRenderResult> RenderAsync(DocsRenderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TemplateBody))
            throw new DocsTemplateException("Template body is empty.");

        ct.ThrowIfCancellationRequested();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var resolved = _templates.Render(request.TemplateBody, request.Vars);
        var pdf = _renderer.Render(resolved, request.Branding, request.DocType);
        var sha = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();

        sw.Stop();
        return Task.FromResult(new DocsRenderResult(pdf, sha, pdf.Length, sw.ElapsedMilliseconds));
    }
}

/// <summary>DI wiring for the Document Generation (M17) stack.</summary>
public static class DocsModule
{
    public static IServiceCollection AddClawbotDocs(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var options = new DocsStorageOptions();
        if (configuration is not null)
        {
            var baseDir = configuration[$"{DocsStorageOptions.SectionName}:BaseDirectory"];
            if (!string.IsNullOrWhiteSpace(baseDir))
                options.BaseDirectory = baseDir;

            var publicUrl = configuration[$"{DocsStorageOptions.SectionName}:PublicBaseUrl"];
            if (!string.IsNullOrWhiteSpace(publicUrl))
                options.PublicBaseUrl = publicUrl;
        }

        services.AddSingleton(options);
        services.AddSingleton<ITemplateEngine, SimpleTemplateEngine>();
        services.AddSingleton<IDocumentRenderer, QuestPdfDocumentRenderer>();
        services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();
        services.AddScoped<DocsAgent>();
        return services;
    }
}
