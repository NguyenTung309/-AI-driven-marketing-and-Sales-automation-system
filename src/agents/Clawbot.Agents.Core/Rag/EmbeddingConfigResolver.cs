namespace Clawbot.Agents.Core.Rag;

public sealed record ResolvedEmbeddingConfig(
    string Provider,
    string ModelId,
    string? ApiKey,
    string? BaseUrl,
    int Dimension,
    string Source,
    string? DisplayName = null)
{
    public bool IsFallback => Source is "hash-fallback";
}

public interface IEmbeddingConfigResolver
{
    Task<ResolvedEmbeddingConfig?> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
