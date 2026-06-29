namespace Clawbot.Agents.Core.Chat;

// Builds a provider-specific chat client per resolved config.
// Anthropic uses a named HttpClient (resilience/timeouts configured at registration);
// OpenAI uses the OpenAI SDK's own pipeline.
public sealed class LlmChatClientFactory(IHttpClientFactory httpClientFactory, bool allowPrivateBaseUrls = false, int httpTimeoutSeconds = 120) : ILlmChatClientFactory
{
    public const string AnthropicHttpClientName = "anthropic-llm";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public IClaudeChatClient Create(ResolvedLlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!string.IsNullOrWhiteSpace(config.BaseUrl) && !LlmBaseUrlGuard.IsAllowedBaseUrl(config.BaseUrl, allowPrivateBaseUrls))
            throw new InvalidOperationException("Configured LLM base URL is not allowed.");

        return config.Provider switch
        {
            "anthropic" => new AnthropicChatClient(CreateAnthropicHttpClient(config), config),
            "openai" or "openai-compatible" => new OpenAiChatClient(config, allowPrivateBaseUrls),
            _ => throw new NotSupportedException($"Unsupported LLM provider '{config.Provider}'."),
        };
    }

    private HttpClient CreateAnthropicHttpClient(ResolvedLlmConfig config)
    {
        // ponytail: per-config TimeoutSeconds applies on the guarded (custom-BaseUrl) path; the pooled named
        // client has a fixed registration-time timeout, so the no-BaseUrl case keeps the global default.
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            return _httpClientFactory.CreateClient(AnthropicHttpClientName);

        return LlmBaseUrlGuard.CreateGuardedHttpClient(new Uri(config.BaseUrl, UriKind.Absolute), allowPrivateBaseUrls, config.TimeoutSeconds ?? httpTimeoutSeconds);
    }
}
