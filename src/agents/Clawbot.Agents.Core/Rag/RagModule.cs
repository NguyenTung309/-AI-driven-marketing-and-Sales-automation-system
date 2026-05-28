using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Agents.Core.Rag;

public static class RagModule
{
    public static IServiceCollection AddClawbotRag(this IServiceCollection services)
    {
        services.AddSingleton<IEmbeddingProvider, HashEmbeddingProvider>();
        services.AddScoped<IRagRetriever, QdrantRagRetriever>();
        return services;
    }
}
