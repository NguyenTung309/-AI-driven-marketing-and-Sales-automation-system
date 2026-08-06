using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Clawbot.Agents.Core.Rag;

public sealed partial class CachedRagRetriever(
    IRagRetriever inner,
    IEmbeddingProvider embedder,
    IConnectionMultiplexer redis,
    IEnumerable<IActiveKbVersionResolver> activeVersionResolvers,
    ILogger<CachedRagRetriever> logger) : IRagRetriever
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
    {
        var cacheKey = await BuildCacheKeyAsync(request, ct).ConfigureAwait(false);
        var db = redis.GetDatabase();

        try
        {
            var cached = await db.StringGetAsync(cacheKey).ConfigureAwait(false);
            if (cached.HasValue)
            {
                var chunks = JsonSerializer.Deserialize<List<RagChunk>>(cached.ToString(), JsonOpts);
                if (chunks is not null)
                {
                    LogCacheHit(logger, cacheKey);
                    return chunks;
                }
            }
        }
        catch (RedisConnectionException ex)
        {
            LogCacheError(logger, ex.Message);
        }

        var result = await inner.RetrieveAsync(request, ct).ConfigureAwait(false);

        // Don't cache empty results: they're cheap to recompute and usually signal a transient
        // misconfiguration (empty collection, just-deployed KB). Caching them for an hour would
        // mask a fresh deploy until the TTL expires.
        if (result.Count == 0)
            return result;

        try
        {
            var json = JsonSerializer.Serialize(result, JsonOpts);
            await db.StringSetAsync(cacheKey, json, CacheTtl).ConfigureAwait(false);
            LogCacheMiss(logger, cacheKey);
        }
        catch (RedisConnectionException ex)
        {
            LogCacheError(logger, ex.Message);
        }

        return result;
    }

    internal async Task<string> BuildCacheKeyAsync(RagRequest request, CancellationToken ct)
    {
        var queryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Query)))[..16];
        var embeddingConfigKey = await ResolveEmbeddingConfigKeyAsync(request.TenantId, ct).ConfigureAwait(false);
        var moduleCodes = request.EffectiveModuleCodes;
        var activeVersionKey = "no-active-resolver";
        if (activeVersionResolvers.FirstOrDefault() is { } activeResolver)
        {
            var ids = await activeResolver.ResolveActiveVersionIdsAsync(request.TenantId, moduleCodes, ct).ConfigureAwait(false);
            activeVersionKey = ids.Count == 0
                ? "no-active-kb"
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', ids.Order(StringComparer.Ordinal)))))[..16];
        }
        var moduleKey = moduleCodes.Count == 0 ? "*" : string.Join('+', moduleCodes.Order(StringComparer.Ordinal));
        return $"rag:{request.TenantId}:{moduleKey}:top{request.TopK}:{embeddingConfigKey}:{activeVersionKey}:{queryHash}";
    }

    private async Task<string> ResolveEmbeddingConfigKeyAsync(Guid tenantId, CancellationToken ct)
    {
        var config = embedder is ConfiguredEmbeddingProvider configured
            ? await configured.ResolveConfigAsync(tenantId, ct).ConfigureAwait(false)
            : new ResolvedEmbeddingConfig("runtime", $"dim-{embedder.Dimension}", null, null, embedder.Dimension, "runtime");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{config.Provider}|{config.ModelId}|{config.Dimension}|{config.BaseUrl}|{config.Source}")))[..16];
        return $"{ConfiguredEmbeddingProvider.CollectionName(config)}:{fingerprint}";
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Debug, Message = "RAG cache HIT: {Key}")]
    private static partial void LogCacheHit(ILogger logger, string key);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Debug, Message = "RAG cache MISS: {Key}")]
    private static partial void LogCacheMiss(ILogger logger, string key);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "RAG cache error: {Reason}")]
    private static partial void LogCacheError(ILogger logger, string reason);
}
