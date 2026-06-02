using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Chat;

public sealed class AnthropicChatClient(HttpClient http, IOptions<AnthropicOptions> options) : IClaudeChatClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = http;
    private readonly AnthropicOptions _opts = options.Value;

    public async Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException("Anthropic:ApiKey not configured.");

        var msgs = new List<MessageBody>(history.Count + 1);
        foreach (var t in history)
            msgs.Add(new MessageBody(t.Role, t.Content));
        msgs.Add(new MessageBody("user", userMessage));

        var payload = new RequestBody(
            Model: _opts.Model,
            MaxTokens: _opts.MaxTokens,
            System: systemPrompt,
            Messages: msgs);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/v1/messages")
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        req.Headers.Add("x-api-key", _opts.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<ResponseBody>(JsonOpts, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty Anthropic response.");

        var text = string.Concat(dto.Content?.Where(c => c.Type == "text").Select(c => c.Text) ?? Array.Empty<string>());
        var inTok = dto.Usage?.InputTokens ?? 0;
        var outTok = dto.Usage?.OutputTokens ?? 0;
        var cost = (inTok * _opts.InputUsdPer1M + outTok * _opts.OutputUsdPer1M) / 1_000_000m;

        return new ClaudeReply(text, inTok, outTok, cost);
    }

    private sealed record MessageBody(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record RequestBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<MessageBody> Messages);

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
