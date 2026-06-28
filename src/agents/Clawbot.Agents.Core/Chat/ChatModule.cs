using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Clawbot.Agents.Core.Chat;

public sealed class LlmBaseUrlOptions
{
    public const string SectionName = "LlmBaseUrl";

    public bool AllowPrivate { get; init; }
}

public sealed class LlmHttpOptions
{
    public const string SectionName = "Llm";

    // Default 120s: the prior 30s cap surfaced long completions as transient failures that exhausted replan rounds (max_rounds).
    public int HttpTimeoutSeconds { get; init; } = 120;
}

public static class ChatModule
{
    public static IServiceCollection AddClawbotChat(this IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Named HttpClient the factory uses to build Anthropic clients. Timeout is configurable (default 120s) so a long
        // completion does not surface as a transient failure that exhausts the orchestrator's replan rounds.
        var httpOptions = cfg.GetSection(LlmHttpOptions.SectionName).Get<LlmHttpOptions>() ?? new LlmHttpOptions();
        services.AddHttpClient(LlmChatClientFactory.AnthropicHttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(httpOptions.HttpTimeoutSeconds);
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
