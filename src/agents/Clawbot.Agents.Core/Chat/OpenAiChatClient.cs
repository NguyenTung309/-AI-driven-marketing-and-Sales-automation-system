using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI;
using OpenAI.Chat;

namespace Clawbot.Agents.Core.Chat;

// OpenAI-compatible chat client (OpenAI, Azure OpenAI, vLLM, local proxies via BaseUrl).
// Reuses the official OpenAI SDK ChatClient — same pattern as ContentLlmClient.
public sealed class OpenAiChatClient : IClaudeChatClient
{
    private const decimal DefaultInputUsdPer1M = 3.00m;
    private const decimal DefaultOutputUsdPer1M = 15.00m;
    private static readonly JsonSerializerOptions DirectJsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly ResolvedLlmConfig _config;
    private readonly ChatClient _client;
    private readonly HttpClient? _directHttp;

    public OpenAiChatClient(ResolvedLlmConfig config, bool allowPrivateBaseUrls = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key not configured.");
        _config = config;
        var apiKey = NormalizeApiKey(config.ApiKey);

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            var endpoint = new Uri(config.BaseUrl, UriKind.Absolute);
            _directHttp = LlmBaseUrlGuard.CreateGuardedHttpClient(endpoint, allowPrivateBaseUrls, config.TimeoutSeconds ?? 120);
            options.Endpoint = endpoint;
            options.Transport = new HttpClientPipelineTransport(_directHttp);
        }

        _client = new ChatClient(config.Model, new ApiKeyCredential(apiKey), options);
    }

    // Test seam: inject a ChatClient backed by a stub transport so the request/usage mapping can be
    // exercised without a live OpenAI endpoint.
    internal OpenAiChatClient(ChatClient client, ResolvedLlmConfig config, HttpClient? directHttp = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _client = client;
        _directHttp = directHttp;
    }

    internal static string NormalizeApiKey(string apiKey)
    {
        const string bearerPrefix = "Bearer ";
        var trimmed = apiKey.Trim();
        return trimmed.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[bearerPrefix.Length..].TrimStart()
            : trimmed;
    }

    private decimal InputRate => _config.InputUsdPer1M ?? DefaultInputUsdPer1M;
    private decimal OutputRate => _config.OutputUsdPer1M ?? DefaultOutputUsdPer1M;
    private decimal Cost(int inTok, int outTok) => (inTok * InputRate + outTok * OutputRate) / 1_000_000m;

    // Fallback ước lượng khi endpoint OpenAI-compatible không trả usage (gateway tự dựng hay bỏ field
    // này). Điều kiện AND: output_tokens = 0 với input_tokens > 0 là số thật của provider, đừng đè.
    private ClaudeReply BuildReply(
        string text,
        int inTok,
        int outTok,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage)
    {
        if (inTok != 0 || outTok != 0)
            return new ClaudeReply(text, inTok, outTok, Cost(inTok, outTok), _config.Model);

        var estimatedIn = LlmTokenEstimator.CountPrompt(_config.Model, systemPrompt, history, userMessage);
        var estimatedOut = LlmTokenEstimator.CountText(_config.Model, text);
        return new ClaudeReply(
            text,
            estimatedIn,
            estimatedOut,
            Cost(estimatedIn, estimatedOut),
            _config.Model,
            IsEstimated: true);
    }

    public async Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default)
    {
        try
        {
            var completion = await _client.CompleteChatAsync(BuildMessages(systemPrompt, history, userMessage), BuildOptions(), ct)
                .ConfigureAwait(false);

            var value = completion.Value;
            var text = string.Concat(value.Content.Select(part => part.Text));
            var inTok = value.Usage?.InputTokenCount ?? 0;
            var outTok = value.Usage?.OutputTokenCount ?? 0;
            return BuildReply(text, inTok, outTok, systemPrompt, history, userMessage);
        }
        catch (Exception ex) when (CanUseDirectFallback(ex))
        {
            return await CompleteDirectAsync(systemPrompt, history, userMessage, ct).ConfigureAwait(false);
        }
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
        var reply = await CompleteAsync(systemPrompt, history, userMessage, ct).ConfigureAwait(false);

        if (reply.Text.Length > 0)
            yield return new ClaudeStreamChunk(reply.Text, Final: false, 0, 0, 0m, reply.Model);

        yield return new ClaudeStreamChunk(string.Empty, Final: true, reply.InputTokens, reply.OutputTokens, reply.UsdCost, reply.Model, reply.IsEstimated);
    }

    // Only emit a token cap when the config sets one explicitly. The OpenAI SDK serializes
    // MaxOutputTokenCount as `max_completion_tokens`; DeepSeek and many OpenAI-compatible gateways
    // only accept `max_tokens` and reject the unknown field (observed as 403). So we don't apply the
    // 3000 default here — the server's own default stands unless the admin opts in.
    private ChatCompletionOptions BuildOptions() =>
        _config.MaxOutputTokens is int max ? new() { MaxOutputTokenCount = max } : new();

    private bool CanUseDirectFallback(Exception ex) =>
        _directHttp is not null
        && !string.IsNullOrWhiteSpace(_config.BaseUrl)
        && IsDirectFallbackEndpoint()
        && IsFallbackStatus(ex);

    private bool IsDirectFallbackEndpoint()
    {
        if (string.Equals(_config.Provider, "openai-compatible", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(_config.Provider, "openai", StringComparison.OrdinalIgnoreCase))
            return false;

        return Uri.TryCreate(_config.BaseUrl, UriKind.Absolute, out var uri)
            && !string.Equals(uri.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFallbackStatus(Exception ex) => ex switch
    {
        ClientResultException { Status: 400 or 401 or 403 or 404 or 422 } => true,
        HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity } => true,
        _ => false,
    };

    private async Task<ClaudeReply> CompleteDirectAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildDirectUrl())
        {
            Content = new StringContent(BuildDirectRequestBody(systemPrompt, history, userMessage), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeApiKey(_config.ApiKey));

        using var response = await _directHttp!.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("OpenAI-compatible fallback failed.", null, response.StatusCode);

        return ParseDirectReply(body, systemPrompt, history, userMessage);
    }

    private string BuildDirectUrl() => _config.BaseUrl!.TrimEnd('/') + "/chat/completions";

    private string BuildDirectRequestBody(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage)
    {
        var messages = new List<DirectMessage>(history.Count + 1);
        foreach (var turn in history)
            messages.Add(new DirectMessage(turn.Role == "assistant" ? "assistant" : "user", [new DirectTextPart("text", turn.Content)]));

        var currentUserMessage = string.IsNullOrWhiteSpace(systemPrompt)
            ? userMessage
            : systemPrompt + "\n\n" + userMessage;
        messages.Add(new DirectMessage("user", [new DirectTextPart("text", currentUserMessage)]));

        return JsonSerializer.Serialize(new DirectRequest(_config.Model, messages, _config.MaxOutputTokens), DirectJsonOptions);
    }

    private ClaudeReply ParseDirectReply(
        string body,
        string systemPrompt = "",
        IReadOnlyList<ChatTurn>? history = null,
        string userMessage = "")
    {
        var parsed = JsonSerializer.Deserialize<DirectResponse>(body);
        var text = ExtractDirectText(parsed?.Choices?.FirstOrDefault()?.Message?.Content);
        var inTok = parsed?.Usage?.PromptTokens ?? 0;
        var outTok = parsed?.Usage?.CompletionTokens ?? 0;
        return BuildReply(text, inTok, outTok, systemPrompt, history ?? [], userMessage);
    }

    private static string ExtractDirectText(JsonElement? content)
    {
        if (content is null)
            return string.Empty;

        var value = content.Value;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        if (value.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var text = new StringBuilder();
        foreach (var part in value.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                text.Append(partText.GetString());
        }

        return text.ToString();
    }

    private sealed record DirectRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<DirectMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens);

    private sealed record DirectMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<DirectTextPart> Content);

    private sealed record DirectTextPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record DirectResponse(
        [property: JsonPropertyName("choices")] DirectChoice[]? Choices,
        [property: JsonPropertyName("usage")] DirectUsage? Usage);

    private sealed record DirectChoice([property: JsonPropertyName("message")] DirectResponseMessage? Message);

    private sealed record DirectResponseMessage([property: JsonPropertyName("content")] JsonElement? Content);

    private sealed record DirectUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);

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
