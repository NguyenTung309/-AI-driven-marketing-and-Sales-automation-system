using Clawbot.Agents.Core.Content.Chain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Content;

public sealed class ContentPromptTemplateOptions
{
    public const string SectionName = "Content:PromptTemplates";

    public IReadOnlyDictionary<string, string> Templates { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class ContentPromptTemplateException : InvalidOperationException
{
    public ContentPromptTemplateException(string message) : base(message) { }
}

public interface IPromptTemplateProvider
{
    string GetTemplate(string platform);
}

internal sealed class ConfigPromptTemplateProvider(IOptions<ContentPromptTemplateOptions> options)
    : IPromptTemplateProvider
{
    private readonly ContentPromptTemplateOptions _options = options.Value;

    public string GetTemplate(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            throw new ArgumentException("platform required", nameof(platform));

        var key = platform.Trim();
        foreach (var template in _options.Templates)
        {
            if (string.Equals(template.Key.Trim(), key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(template.Value))
            {
                return template.Value;
            }
        }

        throw new ContentPromptTemplateException(
            $"Content prompt template for platform '{key}' was not configured.");
    }
}

public static class ContentModule
{
    public static IServiceCollection AddClawbotContent(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IOptions<ContentPromptTemplateOptions>>(
            _ => Options.Create(LoadPromptTemplateOptions(configuration)));
        services.AddSingleton<IPromptTemplateProvider, ConfigPromptTemplateProvider>();
        // ContentAgent now resolves its provider via the scoped IClaudeChatClient (D8) — the old
        // env-config OpenAI client (ContentLlmOptions) is gone. Chat wiring (AddClawbotChat) supplies it.
        services.AddScoped<ContentAgent>();
        // Review-gate P1: LLM reviewer (reviewer-agent binding) cho content output.
        services.AddScoped<ContentReviewer>();

        // Prompt chaining (P1): options nạp phẳng từ Content:Chain (mặc định TẮT), chuỗi + 2 mắt xích.
        // Chuỗi luôn đăng ký nhưng chỉ chạy khi IsEnabledFor(tenant) => an toàn bật dần theo allow-list.
        // Trace sink (EF) do Infrastructure đăng ký; thiếu (host chỉ-Core) thì ContentAgent bỏ qua trace.
        services.AddSingleton<IOptions<ContentChainOptions>>(
            _ => Options.Create(LoadChainOptions(configuration)));
        services.AddScoped<IContentChain, ContentChain>();
        services.AddScoped<IContentChainStep, PlanStep>();
        services.AddScoped<IContentChainStep, OutlineStep>();
        services.AddScoped<IContentChainStep, WriteStep>();
        services.AddScoped<IContentChainStep, PackageStep>();
        return services;
    }

    private static ContentChainOptions LoadChainOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(ContentChainOptions.SectionName);
        var options = new ContentChainOptions();

        var enabled = section.GetValue<bool?>("Enabled");
        if (enabled.HasValue)
            options.Enabled = enabled.Value;

        var version = section["Version"];
        if (!string.IsNullOrWhiteSpace(version))
            options.Version = version.Trim();

        var stepTimeout = section.GetValue<int?>("StepTimeoutSeconds");
        if (stepTimeout is > 0)
            options.StepTimeoutSeconds = stepTimeout.Value;

        var chainTimeout = section.GetValue<int?>("ChainTimeoutSeconds");
        if (chainTimeout is > 0)
            options.ChainTimeoutSeconds = chainTimeout.Value;

        var allowList = ParseTenantAllowList(section.GetSection("TenantAllowList"));
        if (allowList.Count > 0)
            options.TenantAllowList = allowList;

        return options;
    }

    private static List<Guid> ParseTenantAllowList(IConfigurationSection section)
    {
        var result = new List<Guid>();
        foreach (var child in section.GetChildren())
        {
            if (Guid.TryParse(child.Value, out var tenantId))
                result.Add(tenantId);
        }

        return result;
    }

    private static ContentPromptTemplateOptions LoadPromptTemplateOptions(IConfiguration configuration)
    {
        var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in configuration.GetSection(ContentPromptTemplateOptions.SectionName).GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
                templates[child.Key] = child.Value;
        }

        return new ContentPromptTemplateOptions { Templates = templates };
    }
}
