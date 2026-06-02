using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.SaleAssist;

public static class SaleAssistModule
{
    public static IServiceCollection AddClawbotSaleAssist(this IServiceCollection services)
    {
        services.AddScoped<SaleAssistAgent>();
        return services;
    }
}
