using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;

namespace Clawbot.Agents.Core.Rag;

public sealed partial class ConfiguredEmbeddingProvider(
    IEnumerable<IEmbeddingConfigResolver> resolvers,
    IOptions<EmbeddingOptions> options,
    IOptions<LlmBaseUrlOptions> baseUrlOptions,
    IHostEnvironment env,
    ILogger<ConfiguredEmbeddingProvider> logger) : IEmbeddingProvider
{
    private readonly HashEmbeddingProvider _hash = new();
    private readonly IEmbeddingConfigResolver? _resolver = resolvers.FirstOrDefault();
    private readonly EmbeddingOptions _options = options.Value;
    private readonly bool _allowPrivateBaseUrls = env.IsDevelopment() && baseUrlOptions.Value.AllowPrivate;

    // Collection selection now uses actual vector length. This is only a sync fallback for legacy callers.
    public int Dimension => !string.IsNullOrWhiteSpace(_options.ApiKey) ? _options.Dimension : _hash.Dimension;

    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
        EmbedAsync(Guid.Empty, text, ct);

    public async Task<ReadOnlyMemory<float>> EmbedAsync(Guid tenantId, string text, CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(tenantId, ct).ConfigureAwait(false);
        return await EmbedAsync(config, text, ct).ConfigureAwait(false);
    }

    public async Task<ReadOnlyMemory<float>> EmbedAsync(ResolvedEmbeddingConfig config, string text, CancellationToken ct = default)
    {
        if (config.Provider.Equals("hash", StringComparison.OrdinalIgnoreCase))
            return await _hash.EmbedAsync(text, ct).ConfigureAwait(false);

        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            return await EmbedStandardAsync(config, text, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldTryMultimodal(ex))
        {
            // Auto-detect: the standard string-input call failed in a way that signals a multimodal
            // embedding model (HTTP 400, or a 200 body the SDK can't bind → null Value). Retry with the
            // content-array input shape those models expect. Response envelope is identical.
            LogMultimodalFallback(logger, config.ModelId);
            var http = LlmBaseUrlGuard.CreateGuardedHttpClient(EmbeddingsBase(config), _allowPrivateBaseUrls);
            return await EmbedMultimodalHttpAsync(http, EmbeddingsUrl(config), config.ApiKey!, config.ModelId, text, ct).ConfigureAwait(false);
        }
    }

    private async Task<ReadOnlyMemory<float>> EmbedStandardAsync(ResolvedEmbeddingConfig config, string text, CancellationToken ct)
    {
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            var endpoint = new Uri(config.BaseUrl, UriKind.Absolute);
            clientOptions.Endpoint = endpoint;
            clientOptions.Transport = new HttpClientPipelineTransport(
                LlmBaseUrlGuard.CreateGuardedHttpClient(endpoint, _allowPrivateBaseUrls));
        }

        var client = new EmbeddingClient(config.ModelId, new ApiKeyCredential(config.ApiKey!), clientOptions);
        var result = await client.GenerateEmbeddingAsync(text, new EmbeddingGenerationOptions
        {
            Dimensions = config.Dimension,
        }, ct).ConfigureAwait(false);

        var embedding = result.Value.ToFloats().ToArray();
        LogEmbedded(logger, config.Source, config.ModelId, text.Length, embedding.Length);
        return embedding;
    }

    private static string BaseOrDefault(ResolvedEmbeddingConfig config) =>
        string.IsNullOrWhiteSpace(config.BaseUrl) ? "https://api.openai.com/v1" : config.BaseUrl;

    private static Uri EmbeddingsBase(ResolvedEmbeddingConfig config) => new(BaseOrDefault(config), UriKind.Absolute);

    private static string EmbeddingsUrl(ResolvedEmbeddingConfig config) => BaseOrDefault(config).TrimEnd('/') + "/embeddings";

    // True when a standard embeddings call failed in a way that suggests the model wants multimodal
    // (content-array) input: a 400 from the server, or a 200 whose body the SDK couldn't bind (null Value).
    public static bool ShouldTryMultimodal(Exception ex) =>
        (ex is ClientResultException c && c.Status == 400)
        || ex.Message.Contains("ClientResult", StringComparison.Ordinal);

    // Raw embeddings POST using the multimodal content-array input shape. Response is the standard
    // OpenAI {data:[{embedding:[...]}]} envelope, so one parse covers any compatible gateway.
    public static async Task<float[]> EmbedMultimodalHttpAsync(
        HttpClient http, string embeddingsUrl, string apiKey, string model, string text, CancellationToken ct)
    {
        var body = new
        {
            model,
            input = new[] { new { content = new[] { new { type = "text", text } } } },
            encoding_format = "float",
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, embeddingsUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"multimodal embeddings HTTP {(int)resp.StatusCode}: {TruncateBody(json)}");

        // Provider có thể trả 200 nhưng body là envelope lỗi ({"error":{...}}, hay gặp ở OpenRouter)
        // hoặc shape không có "data" — GetProperty thẳng nổ KeyNotFoundException không kèm manh mối.
        // Đọc phòng thủ và ném lỗi mang theo body (cắt ngắn) để chẩn đoán được từ log/UI.
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"multimodal embeddings non-JSON response: {TruncateBody(json)}");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() == 0
                || !data[0].TryGetProperty("embedding", out var emb)
                || emb.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"multimodal embeddings unexpected response: {TruncateBody(json)}");
            }

            var vec = new float[emb.GetArrayLength()];
            var i = 0;
            foreach (var v in emb.EnumerateArray()) vec[i++] = v.GetSingle();
            return vec;
        }
    }

    // Body lỗi đưa vào exception message phải cắt ngắn — response bất thường có thể là trang HTML dài.
    private const int MaxErrorBodyChars = 500;

    private static string TruncateBody(string value) =>
        value.Length <= MaxErrorBodyChars ? value : string.Concat(value.AsSpan(0, MaxErrorBodyChars), "…");

    public Task<ResolvedEmbeddingConfig> ResolveConfigAsync(CancellationToken ct = default) =>
        ResolveConfigAsync(Guid.Empty, ct);

    public static string CollectionName(ResolvedEmbeddingConfig config) =>
        $"kb_{SafeKey(config.Provider)}_{SafeKey(config.ModelId)}_v{config.Dimension}";

    private static string SafeKey(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        return new string(chars).Trim('_');
    }

    public async Task<ResolvedEmbeddingConfig> ResolveConfigAsync(Guid tenantId, CancellationToken ct = default)
    {
        var configured = _resolver is null ? null : await _resolver.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (configured is not null)
            return configured;

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new ResolvedEmbeddingConfig(
                "openai",
                string.IsNullOrWhiteSpace(_options.Model) ? "text-embedding-3-small" : _options.Model,
                _options.ApiKey,
                string.IsNullOrWhiteSpace(_options.BaseUrl) ? null : _options.BaseUrl,
                _options.Dimension <= 0 ? 1536 : _options.Dimension,
                "appsettings");
        }

        return new ResolvedEmbeddingConfig("hash", "hash-384", null, null, _hash.Dimension, "hash-fallback", "Hash fallback");
    }

    [LoggerMessage(EventId = 7002, Level = LogLevel.Debug,
        Message = "Embedded via {Source}:{ModelId} {CharCount} chars → {Dim}-dim vector")]
    private static partial void LogEmbedded(ILogger logger, string source, string modelId, int charCount, int dim);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information,
        Message = "Standard embeddings failed for {ModelId}; retrying with multimodal content-array input")]
    private static partial void LogMultimodalFallback(ILogger logger, string modelId);
}
