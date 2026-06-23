using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using OpenAI;
using OpenAI.Chat;

namespace Clawbot.Agents.Core.Chat;

// OpenAI-compatible chat client (OpenAI, Azure OpenAI, vLLM, local proxies via BaseUrl).
// Reuses the official OpenAI SDK ChatClient — same pattern as ContentLlmClient.
public sealed class OpenAiChatClient : IClaudeChatClient
{
    private const decimal DefaultInputUsdPer1M = 3.00m;
    private const decimal DefaultOutputUsdPer1M = 15.00m;

    private readonly ResolvedLlmConfig _config;
    private readonly ChatClient _client;

    public OpenAiChatClient(ResolvedLlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key not configured.");
        _config = config;

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            var endpoint = new Uri(config.BaseUrl, UriKind.Absolute);
            options.Endpoint = endpoint;
            options.Transport = new HttpClientPipelineTransport(LlmBaseUrlGuard.CreateGuardedHttpClient(endpoint));
        }

        _client = new ChatClient(config.Model, new ApiKeyCredential(config.ApiKey), options);
    }

    // Test seam: inject a ChatClient backed by a stub transport so the request/usage mapping can be
    // exercised without a live OpenAI endpoint.
    internal OpenAiChatClient(ChatClient client, ResolvedLlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _client = client;
    }

    private decimal InputRate => _config.InputUsdPer1M ?? DefaultInputUsdPer1M;
    private decimal OutputRate => _config.OutputUsdPer1M ?? DefaultOutputUsdPer1M;
    private decimal Cost(int inTok, int outTok) => (inTok * InputRate + outTok * OutputRate) / 1_000_000m;

    public async Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default)
    {
        var completion = await _client.CompleteChatAsync(BuildMessages(systemPrompt, history, userMessage), BuildOptions(), ct)
            .ConfigureAwait(false);

        var value = completion.Value;
        var text = string.Concat(value.Content.Select(part => part.Text));
        var inTok = value.Usage?.InputTokenCount ?? 0;
        var outTok = value.Usage?.OutputTokenCount ?? 0;
        return new ClaudeReply(text, inTok, outTok, Cost(inTok, outTok), _config.Model);
    }

    // OpenAI SDK 2.11.0 exposes no public way to request streaming token-usage
    // (ChatCompletionOptions.StreamOptions / IncludeUsage are internal), so a real token stream
    // would report zero usage and silently bypass the cost cap. We resolve the full completion
    // (usage IS returned here) and surface it as a single content chunk + a final usage chunk.
    // Trade-off: OpenAI replies arrive all-at-once; the Anthropic path still streams incrementally.
    public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var completion = await _client.CompleteChatAsync(BuildMessages(systemPrompt, history, userMessage), BuildOptions(), ct)
            .ConfigureAwait(false);

        var value = completion.Value;
        var text = string.Concat(value.Content.Select(part => part.Text));
        var inTok = value.Usage?.InputTokenCount ?? 0;
        var outTok = value.Usage?.OutputTokenCount ?? 0;

        if (text.Length > 0)
            yield return new ClaudeStreamChunk(text, Final: false, 0, 0, 0m, _config.Model);

        yield return new ClaudeStreamChunk(string.Empty, Final: true, inTok, outTok, Cost(inTok, outTok), _config.Model);
    }

    private static ChatCompletionOptions BuildOptions() => new();

    private static List<ChatMessage> BuildMessages(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage)
    {
        var messages = new List<ChatMessage>(history.Count + 2);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(ChatMessage.CreateSystemMessage(systemPrompt));
        foreach (var turn in history)
        {
            messages.Add(turn.Role == "assistant"
                ? ChatMessage.CreateAssistantMessage(turn.Content)
                : ChatMessage.CreateUserMessage(turn.Content));
        }
        messages.Add(ChatMessage.CreateUserMessage(userMessage));
        return messages;
    }
}
