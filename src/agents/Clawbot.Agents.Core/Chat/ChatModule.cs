using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Chat;

public static class ChatModule
{
    public static IServiceCollection AddClawbotChat(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<AnthropicOptions>(cfg.GetSection(AnthropicOptions.SectionName));
        services.AddHttpClient<IClaudeChatClient, AnthropicChatClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<ChatAgent>();
        return services;
    }
}
