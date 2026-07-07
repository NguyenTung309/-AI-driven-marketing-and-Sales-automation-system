using Clawbot.SharedKernel.Vectors;
using Microsoft.Extensions.Logging;

namespace Clawbot.Agents.Core.Rag;

public sealed partial class QdrantRagRetriever(
    IVectorStore store,
    IEmbeddingProvider embedder,
    IEnumerable<IActiveKbVersionResolver> activeVersionResolvers,
    ILogger<QdrantRagRetriever> logger) : IRagRetriever
{
    public async Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return Array.Empty<RagChunk>();

        var embeddingConfig = embedder is ConfiguredEmbeddingProvider configured
            ? await configured.ResolveConfigAsync(request.TenantId, ct).ConfigureAwait(false)
            : new ResolvedEmbeddingConfig("runtime", $"dim-{embedder.Dimension}", null, null, embedder.Dimension, "runtime");
        var queryVec = embedder is ConfiguredEmbeddingProvider configuredEmbedder
            ? await configuredEmbedder.EmbedAsync(embeddingConfig, request.Query, ct).ConfigureAwait(false)
            : await embedder.EmbedAsync(request.Query, ct).ConfigureAwait(false);
        var collection = ConfiguredEmbeddingProvider.CollectionName(embeddingConfig);

        var activeVersionIds = activeVersionResolvers.FirstOrDefault() is { } activeResolver
            ? await activeResolver.ResolveActiveVersionIdsAsync(request.TenantId, request.KbModuleCode, ct).ConfigureAwait(false)
            : null;
        if (activeVersionIds is not null && activeVersionIds.Count == 0) return Array.Empty<RagChunk>();

        var tenantTag = request.TenantId.ToString();
        var filters = new List<VectorMetadataFilter>
        {
            new("tenant_id", [tenantTag]),
        };
        if (!string.IsNullOrEmpty(request.KbModuleCode))
            filters.Add(new VectorMetadataFilter("module_code", [request.KbModuleCode]));
        if (activeVersionIds is not null)
            filters.Add(new VectorMetadataFilter("kb_version_id", activeVersionIds.ToArray()));

        var hits = await store.SearchAsync(collection, queryVec, request.TopK, filters, ct).ConfigureAwait(false);
        var array = queryVec.ToArray();
        var hasNaN = false;
        double sumSq = 0;
        for (var k = 0; k < array.Length; k++)
        {
            if (float.IsNaN(array[k]) || float.IsInfinity(array[k])) { hasNaN = true; break; }
            sumSq += array[k] * array[k];
        }
        LogRetrieve(logger, collection, queryVec.Length, activeVersionIds?.Count ?? -1, hits.Count, hasNaN, Math.Sqrt(sumSq), request.TenantId, request.KbModuleCode ?? "");
        if (hits.Count == 0)
        {
            var probe = await store.SearchAsync(collection, queryVec, request.TopK, null, ct).ConfigureAwait(false);
            LogRetrieveProbe(logger, collection, probe.Count);
        }
        var filtered = hits
            .Where(h => h.Metadata.TryGetValue("tenant_id", out var t) && string.Equals(t, tenantTag, StringComparison.Ordinal))
            .Where(h => string.IsNullOrEmpty(request.KbModuleCode)
                || (h.Metadata.TryGetValue("module_code", out var m) && string.Equals(m, request.KbModuleCode, StringComparison.Ordinal)))
            .Where(h => activeVersionIds is null
                || (h.Metadata.TryGetValue("kb_version_id", out var v) && activeVersionIds.Contains(v)))
            .Take(request.TopK)
            .Select(h => new RagChunk(
                KbVersionId: h.Metadata.TryGetValue("kb_version_id", out var vid) ? vid : h.Id,
                KbModuleCode: h.Metadata.TryGetValue("module_code", out var mc) ? mc : string.Empty,
                Snippet: h.Metadata.TryGetValue("snippet", out var s) ? s : string.Empty,
                Score: h.Score))
            .ToList();

        return filtered;
    }

    [LoggerMessage(EventId = 8101, Level = LogLevel.Information,
        Message = "RAG retrieve: collection={Collection} queryDim={QueryDim} activeVersions={ActiveCount} rawHits={RawHits} hasNaN={HasNaN} magnitude={Magnitude} tenant={TenantId} module={ModuleCode}")]
    private static partial void LogRetrieve(
        ILogger logger, string collection, int queryDim, int activeCount, int rawHits, bool hasNaN, double magnitude, Guid tenantId, string moduleCode);

    [LoggerMessage(EventId = 8102, Level = LogLevel.Information,
        Message = "RAG retrieve probe (no filter): collection={Collection} probeHits={ProbeHits}")]
    private static partial void LogRetrieveProbe(ILogger logger, string collection, int probeHits);
}
