using System.ClientModel;
using System.ClientModel.Primitives;
using Clawbot.Agents.Core.Chat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;

namespace Clawbot.Agents.Core.Rag;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "text-embedding-3-small";
    public int Dimension { get; set; } = 1536;
}

public sealed partial class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingOptions _options;
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;

    public int Dimension => _options.Dimension;

    public OpenAiEmbeddingProvider(IOptions<EmbeddingOptions> options, ILogger<OpenAiEmbeddingProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Embedding:ApiKey not configured.");

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            var endpoint = new Uri(_options.BaseUrl, UriKind.Absolute);
            clientOptions.Endpoint = endpoint;
            clientOptions.Transport = new HttpClientPipelineTransport(
                LlmBaseUrlGuard.CreateGuardedHttpClient(endpoint));
        }

        _client = new EmbeddingClient(_options.Model, new ApiKeyCredential(_options.ApiKey), clientOptions);
    }

    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var result = await _client.GenerateEmbeddingAsync(text, new EmbeddingGenerationOptions
        {
            Dimensions = _options.Dimension,
        }, ct).ConfigureAwait(false);

        var embedding = result.Value.ToFloats();
        LogEmbedded(_logger, text.Length, embedding.Length);

        return embedding.ToArray();
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Debug,
        Message = "Embedded {CharCount} chars → {Dim}-dim vector")]
    private static partial void LogEmbedded(ILogger logger, int charCount, int dim);
}
