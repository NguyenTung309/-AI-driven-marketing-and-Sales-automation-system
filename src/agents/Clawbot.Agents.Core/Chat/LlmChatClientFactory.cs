namespace Clawbot.Agents.Core.Chat;

// Builds a provider-specific chat client per resolved config.
// Anthropic uses a named HttpClient (resilience/timeouts configured at registration);
// OpenAI uses the OpenAI SDK's own pipeline.
public sealed class LlmChatClientFactory(IHttpClientFactory httpClientFactory) : ILlmChatClientFactory
{
    public const string AnthropicHttpClientName = "anthropic-llm";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public IClaudeChatClient Create(ResolvedLlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!string.IsNullOrWhiteSpace(config.BaseUrl) && !LlmBaseUrlGuard.IsAllowedBaseUrl(config.BaseUrl))
            throw new InvalidOperationException("Configured LLM base URL is not allowed.");

        return config.Provider switch
        {
            "anthropic" => new AnthropicChatClient(CreateAnthropicHttpClient(config), config),
            "openai" => new OpenAiChatClient(config),
            _ => throw new NotSupportedException($"Unsupported LLM provider '{config.Provider}'."),
        };
    }

    private HttpClient CreateAnthropicHttpClient(ResolvedLlmConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            return _httpClientFactory.CreateClient(AnthropicHttpClientName);

        return LlmBaseUrlGuard.CreateGuardedHttpClient(new Uri(config.BaseUrl, UriKind.Absolute));
    }
}
