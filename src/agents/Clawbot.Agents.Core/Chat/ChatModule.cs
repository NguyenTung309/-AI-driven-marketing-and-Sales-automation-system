using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Chat;

public static class ChatModule
{
    public static IServiceCollection AddClawbotChat(this IServiceCollection services, IConfiguration cfg)
    {
        // Named HttpClient the factory uses to build Anthropic clients (timeout configured here).
        services.AddHttpClient(LlmChatClientFactory.AnthropicHttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        // Per-(tenant, agent) provider resolution. ILlmConfigResolver is registered in AddInfrastructure
        // (it needs AppDbContext + IEncryptor). The delegating client reads the ambient call scope.
        services.AddSingleton<ILlmCallScope, LlmCallScope>();
        services.AddSingleton<ILlmChatClientFactory, LlmChatClientFactory>();
        services.AddSingleton<IClaudeChatClient, ScopedLlmChatClient>();

        services.AddScoped<ChatAgent>();
        return services;
    }
}
