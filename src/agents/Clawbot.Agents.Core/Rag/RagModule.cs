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

        services.AddScoped<IEmbeddingProvider, ConfiguredEmbeddingProvider>();

        services.AddScoped<QdrantRagRetriever>();
        services.AddScoped<IRagRetriever>(sp =>
        {
            IRagRetriever inner = sp.GetRequiredService<QdrantRagRetriever>();
            var redis = sp.GetService<StackExchange.Redis.IConnectionMultiplexer>();
            if (redis is not null)
                inner = new CachedRagRetriever(
                    inner,
                    sp.GetRequiredService<IEmbeddingProvider>(),
                    redis,
                    sp.GetServices<IActiveKbVersionResolver>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedRagRetriever>>());
            return inner;
        });

        services.AddScoped<KbDeployService>();
        return services;
    }
}
