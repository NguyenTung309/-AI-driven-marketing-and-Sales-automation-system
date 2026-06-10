using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Clawbot.Agents.Core.Rag;

public sealed partial class CachedRagRetriever(
    IRagRetriever inner,
    IConnectionMultiplexer redis,
    ILogger<CachedRagRetriever> logger) : IRagRetriever
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(request);
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

    private static string BuildCacheKey(RagRequest request)
    {
        var queryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Query)))[..16];
        return $"rag:{request.TenantId}:{request.KbModuleCode ?? "*"}:{queryHash}";
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Debug, Message = "RAG cache HIT: {Key}")]
    private static partial void LogCacheHit(ILogger logger, string key);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Debug, Message = "RAG cache MISS: {Key}")]
    private static partial void LogCacheMiss(ILogger logger, string key);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "RAG cache error: {Reason}")]
    private static partial void LogCacheError(ILogger logger, string reason);
}
