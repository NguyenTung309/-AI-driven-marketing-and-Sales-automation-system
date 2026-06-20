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
        return services;
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
