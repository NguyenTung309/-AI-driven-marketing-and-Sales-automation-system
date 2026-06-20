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
        return config.Provider switch
        {
            "anthropic" => new AnthropicChatClient(_httpClientFactory.CreateClient(AnthropicHttpClientName), config),
            "openai" => new OpenAiChatClient(config),
            _ => throw new NotSupportedException($"Unsupported LLM provider '{config.Provider}'."),
        };
    }
}
