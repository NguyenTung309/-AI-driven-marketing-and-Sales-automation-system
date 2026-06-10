using Clawbot.Agents.Core.Kb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Rag;

public static class RagModule
{
    public static IServiceCollection AddClawbotRag(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<EmbeddingOptions>(cfg.GetSection(EmbeddingOptions.SectionName));

        var embeddingCfg = cfg.GetSection(EmbeddingOptions.SectionName);
        if (!string.IsNullOrWhiteSpace(embeddingCfg["ApiKey"]))
        {
            services.AddSingleton<IEmbeddingProvider, OpenAiEmbeddingProvider>();
        }
        else
        {
            services.AddSingleton<IEmbeddingProvider, HashEmbeddingProvider>();
        }

        services.AddScoped<QdrantRagRetriever>();
        services.AddScoped<IRagRetriever>(sp =>
        {
            IRagRetriever inner = sp.GetRequiredService<QdrantRagRetriever>();
            var redis = sp.GetService<StackExchange.Redis.IConnectionMultiplexer>();
            if (redis is not null)
                inner = new CachedRagRetriever(inner, redis, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedRagRetriever>>());
            return inner;
        });

        services.AddScoped<KbDeployService>();
        return services;
    }
}
