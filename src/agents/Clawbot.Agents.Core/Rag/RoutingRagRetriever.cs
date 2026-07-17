using Microsoft.Extensions.Logging;

namespace Clawbot.Agents.Core.Rag;

// Chọn đường truy xuất KB theo cấu hình embedding của tenant (QĐ user 2026-07-16):
// - Tenant có embedding config thật (tenant-db/appsettings) -> vector search Qdrant như cũ.
// - Không cấu hình gì (hash-fallback) -> LLM mode: dùng chính LLM chat của tenant chọn đoạn KB.
// Trước đây hash-fallback vẫn đi Qdrant với vector giả — tìm sai âm thầm.
public sealed partial class RoutingRagRetriever(
    IRagRetriever vectorRetriever,
    LlmRagRetriever llmRetriever,
    ConfiguredEmbeddingProvider embeddingProvider,
    ILogger<RoutingRagRetriever> logger) : IRagRetriever
{
    private readonly IRagRetriever _vectorRetriever = vectorRetriever;
    private readonly LlmRagRetriever _llmRetriever = llmRetriever;
    private readonly ConfiguredEmbeddingProvider _embeddingProvider = embeddingProvider;
    private readonly ILogger<RoutingRagRetriever> _logger = logger;

    public async Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var config = await _embeddingProvider.ResolveConfigAsync(request.TenantId, ct).ConfigureAwait(false);
        if (config.IsFallback)
        {
            LogRoutedLlm(_logger, request.TenantId);
            return await _llmRetriever.RetrieveAsync(request, ct).ConfigureAwait(false);
        }

        return await _vectorRetriever.RetrieveAsync(request, ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 7013, Level = LogLevel.Debug,
        Message = "KB retrieval routed to LLM mode for tenant {TenantId} (no embedding config)")]
    private static partial void LogRoutedLlm(ILogger logger, Guid tenantId);
}
