using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Clawbot.Agents.Core.Chat;

public sealed class LlmBaseUrlOptions
{
    public const string SectionName = "LlmBaseUrl";

    public bool AllowPrivate { get; init; }
}

public static class ChatModule
{
    public static IServiceCollection AddClawbotChat(this IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Named HttpClient the factory uses to build Anthropic clients (timeout configured here).
        services.AddHttpClient(LlmChatClientFactory.AnthropicHttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        // Per-(tenant, agent) provider resolution. ILlmConfigResolver is registered in AddInfrastructure
        // (it needs AppDbContext + IEncryptor). The delegating client reads the ambient call scope.
        var baseUrlOptions = cfg.GetSection(LlmBaseUrlOptions.SectionName).Get<LlmBaseUrlOptions>() ?? new LlmBaseUrlOptions();
        var allowPrivateBaseUrls = env.IsDevelopment() && baseUrlOptions.AllowPrivate;
        services.AddSingleton<ILlmCallScope, LlmCallScope>();
        services.AddSingleton<ILlmChatClientFactory>(sp => new LlmChatClientFactory(sp.GetRequiredService<IHttpClientFactory>(), allowPrivateBaseUrls));
        services.AddSingleton<IClaudeChatClient, ScopedLlmChatClient>();

        services.AddScoped<ChatAgent>();
        return services;
    }
}
