using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawbot.Agents.Core.Chat;

// Per-call client bound to a resolved provider config (built by ILlmChatClientFactory).
// No IOptions/env credential read — the key/model/baseUrl/rates all come from ResolvedLlmConfig (D1/D3).
public sealed class AnthropicChatClient(HttpClient http, ResolvedLlmConfig config) : IClaudeChatClient
{
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const int DefaultMaxTokens = 3000;
    private const decimal DefaultInputUsdPer1M = 3.00m;
    private const decimal DefaultOutputUsdPer1M = 15.00m;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = http;
    private readonly ResolvedLlmConfig _config = config;

    private string BaseUrl => string.IsNullOrWhiteSpace(_config.BaseUrl) ? DefaultBaseUrl : _config.BaseUrl;
    private decimal InputRate => _config.InputUsdPer1M ?? DefaultInputUsdPer1M;
    private decimal OutputRate => _config.OutputUsdPer1M ?? DefaultOutputUsdPer1M;

    public async Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException("Anthropic API key not configured.");

        using var req = CreateRequest(systemPrompt, history, userMessage, "application/json", stream: false);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<ResponseBody>(JsonOpts, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty Anthropic response.");

        var text = string.Concat(dto.Content?.Where(c => c.Type == "text").Select(c => c.Text) ?? Array.Empty<string>());
        var inTok = dto.Usage?.InputTokens ?? 0;
        var outTok = dto.Usage?.OutputTokens ?? 0;

        if (inTok != 0 || outTok != 0)
            return new ClaudeReply(text, inTok, outTok, CalculateCost(inTok, outTok), _config.Model);

        // Anthropic thật luôn trả usage; nhánh này chỉ chạy khi đứng sau proxy làm mất field usage.
        var estimatedIn = LlmTokenEstimator.CountPrompt(_config.Model, systemPrompt, history, userMessage);
        var estimatedOut = LlmTokenEstimator.CountText(_config.Model, text);
        return new ClaudeReply(
            text,
            estimatedIn,
            estimatedOut,
            CalculateCost(estimatedIn, estimatedOut),
            _config.Model,
            IsEstimated: true);
    }

    public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException("Anthropic API key not configured.");

        using var req = CreateRequest(systemPrompt, history, userMessage, "text/event-stream", stream: true);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await foreach (var chunk in ReadStreamChunksAsync(resp.Content, systemPrompt, history, userMessage, ct).ConfigureAwait(false))
            yield return chunk;
    }

    private HttpRequestMessage CreateRequest(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        string acceptMediaType,
        bool stream)
    {
        var msgs = new List<MessageBody>(history.Count + 1);
        foreach (var t in history)
            msgs.Add(new MessageBody(t.Role, t.Content));
        msgs.Add(new MessageBody("user", userMessage));

        var payload = new RequestBody(
            Model: _config.Model,
            MaxTokens: _config.MaxOutputTokens ?? DefaultMaxTokens,
            System: systemPrompt,
            Messages: msgs,
            Stream: stream);

        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl.TrimEnd('/')}/v1/messages")
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        req.Headers.Add("x-api-key", _config.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptMediaType));
        return req;
    }

    private async IAsyncEnumerable<ClaudeStreamChunk> ReadStreamChunksAsync(
        HttpContent content,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var data = new StringBuilder();
        var visible = new StringBuilder();
        var inputTokens = 0;
        var outputTokens = 0;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
                break;

            if (line.Length == 0)
            {
                if (TryReadStreamChunk(data, ref inputTokens, ref outputTokens) is { } chunk)
                {
                    visible.Append(chunk.Text);
                    yield return chunk;
                }
                data.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(line[5..].TrimStart());
            }
        }

        if (TryReadStreamChunk(data, ref inputTokens, ref outputTokens) is { } finalDataChunk)
        {
            visible.Append(finalDataChunk.Text);
            yield return finalDataChunk;
        }

        var estimated = inputTokens == 0 && outputTokens == 0;
        if (estimated)
        {
            inputTokens = LlmTokenEstimator.CountPrompt(_config.Model, systemPrompt, history, userMessage);
            outputTokens = LlmTokenEstimator.CountText(_config.Model, visible.ToString());
        }

        yield return new ClaudeStreamChunk(
            string.Empty,
            Final: true,
            inputTokens,
            outputTokens,
            CalculateCost(inputTokens, outputTokens),
            _config.Model,
            IsEstimated: estimated);
    }

    private decimal CalculateCost(int inputTokens, int outputTokens) =>
        (inputTokens * InputRate + outputTokens * OutputRate) / 1_000_000m;

    private static ClaudeStreamChunk? TryReadStreamChunk(StringBuilder data, ref int inputTokens, ref int outputTokens)
    {
        if (data.Length == 0)
            return null;

        var json = data.ToString();
        if (json.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeElement))
            return null;

        switch (typeElement.GetString())
        {
            case "message_start":
                if (root.TryGetProperty("message", out var message)
                    && message.TryGetProperty("usage", out var startUsage)
                    && startUsage.TryGetProperty("input_tokens", out var input))
                {
                    inputTokens = input.GetInt32();
                }
                return null;

            case "content_block_delta":
                if (root.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("type", out var deltaType)
                    && deltaType.GetString() == "text_delta"
                    && delta.TryGetProperty("text", out var text))
                {
                    return new ClaudeStreamChunk(text.GetString() ?? string.Empty, Final: false, 0, 0, 0m);
                }
                return null;

            case "message_delta":
                if (root.TryGetProperty("usage", out var deltaUsage)
                    && deltaUsage.TryGetProperty("output_tokens", out var output))
                {
                    outputTokens = output.GetInt32();
                }
                return null;

            default:
                return null;
        }
    }

    private sealed record MessageBody(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record RequestBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<MessageBody> Messages,
        [property: JsonPropertyName("stream"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Stream = false);

    private sealed record ResponseBody(
        [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock>? Content,
        [property: JsonPropertyName("usage")] UsageBlock? Usage);

    private sealed record ContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record UsageBlock(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}
