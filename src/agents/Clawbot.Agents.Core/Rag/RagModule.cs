using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Kb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Rag;

public static class RagModule
{
    public static IServiceCollection AddClawbotRag(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<EmbeddingOptions>(cfg.GetSection(EmbeddingOptions.SectionName));
        services.Configure<LlmBaseUrlOptions>(cfg.GetSection(LlmBaseUrlOptions.SectionName));

        // Concrete + forward: RoutingRagRetriever cần ResolveConfigAsync (không nằm trên IEmbeddingProvider).
        services.AddScoped<ConfiguredEmbeddingProvider>();
        services.AddScoped<IEmbeddingProvider>(sp => sp.GetRequiredService<ConfiguredEmbeddingProvider>());

        services.AddScoped<QdrantRagRetriever>();
        services.AddScoped<LlmRagRetriever>();
        services.AddScoped<IRagRetriever>(sp =>
        {
            IRagRetriever vector = sp.GetRequiredService<QdrantRagRetriever>();
            var redis = sp.GetService<StackExchange.Redis.IConnectionMultiplexer>();
            if (redis is not null)
                vector = new CachedRagRetriever(
                    vector,
                    sp.GetRequiredService<IEmbeddingProvider>(),
                    redis,
                    sp.GetServices<IActiveKbVersionResolver>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedRagRetriever>>());

            // Mặc định (không embedding config) -> LLM mode; có config thật -> vector mode như cũ.
            return new RoutingRagRetriever(
                vector,
                sp.GetRequiredService<LlmRagRetriever>(),
                sp.GetRequiredService<ConfiguredEmbeddingProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RoutingRagRetriever>>());
        });

        services.AddScoped<KbDeployService>();
        return services;
    }
}
