using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawbot.Agents.Core.Chat;

// OpenAI Responses API client (POST {baseUrl}/responses) — chuẩn "v2" thay Chat Completions
// (developers.openai.com/api/docs/guides/migrate-to-responses). Dùng cho gateway chỉ expose
// /v1/responses. Direct HTTP thay vì SDK: mapping tối thiểu (instructions + input items),
// cùng pattern direct-fallback của OpenAiChatClient.
public sealed class OpenAiResponsesChatClient : IClaudeChatClient
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";
    private const decimal DefaultInputUsdPer1M = 3.00m;
    private const decimal DefaultOutputUsdPer1M = 15.00m;
    private static readonly JsonSerializerOptions JsonOpts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly ResolvedLlmConfig _config;
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public OpenAiResponsesChatClient(ResolvedLlmConfig config, bool allowPrivateBaseUrls = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key not configured.");
        _config = config;
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultBaseUrl : config.BaseUrl.TrimEnd('/');
        _http = LlmBaseUrlGuard.CreateGuardedHttpClient(new Uri(_baseUrl, UriKind.Absolute), allowPrivateBaseUrls, config.TimeoutSeconds ?? 120);
    }

    // Test seam: stub HttpClient để test mapping request/response không cần endpoint thật.
    internal OpenAiResponsesChatClient(HttpClient http, ResolvedLlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _http = http;
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? DefaultBaseUrl : config.BaseUrl.TrimEnd('/');
    }

    private decimal InputRate => _config.InputUsdPer1M ?? DefaultInputUsdPer1M;
    private decimal OutputRate => _config.OutputUsdPer1M ?? DefaultOutputUsdPer1M;
    private decimal Cost(int inTok, int outTok) => (inTok * InputRate + outTok * OutputRate) / 1_000_000m;

    // Luôn gửi stream:true — một số gateway (quan sát aigatewayport 2026-07) trả stub KHÔNG có
    // `output` khi gọi non-stream; SSE là đường duy nhất lấy nội dung. Server chuẩn OpenAI xử lý
    // stream tốt như nhau; nếu server trả JSON thường (không phải text/event-stream) thì parse như cũ.
    public async Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/responses")
        {
            Content = new StringContent(BuildRequestBody(systemPrompt, history, userMessage), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OpenAiChatClient.NormalizeApiKey(_config.ApiKey));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("OpenAI Responses API call failed.", null, response.StatusCode);

        var isEventStream = string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        if (!isEventStream)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseReply(body, systemPrompt, history, userMessage);
        }

        return await ReadSseReplyAsync(response, systemPrompt, history, userMessage, ct).ConfigureAwait(false);
    }

    // Gom SSE: response.output_text.delta -> text hiển thị; response.completed -> usage.
    // reasoning_summary_text.delta gom riêng: KHÔNG đưa vào text trả về, chỉ dùng để ước lượng
    // output token khi gateway không trả usage (probe: 403 reasoning delta / 4 output delta —
    // nếu chỉ đếm text hiển thị thì ước lượng lệch hàng chục lần).
    private async Task<ClaudeReply> ReadSseReplyAsync(
        HttpResponseMessage response,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct)
    {
        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        var inTok = 0;
        var outTok = 0;

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line["data: ".Length..];
            if (payload == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (string.Equals(type, "response.output_text.delta", StringComparison.Ordinal))
                {
                    if (doc.RootElement.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                        text.Append(delta.GetString());
                }
                else if (string.Equals(type, "response.reasoning_summary_text.delta", StringComparison.Ordinal))
                {
                    if (doc.RootElement.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                        reasoning.Append(delta.GetString());
                }
                else if (string.Equals(type, "response.completed", StringComparison.Ordinal))
                {
                    if (doc.RootElement.TryGetProperty("response", out var resp)
                        && resp.TryGetProperty("usage", out var usage))
                    {
                        if (usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out var i)) inTok = i;
                        if (usage.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt32(out var o)) outTok = o;
                    }
                    break;
                }
                else if (string.Equals(type, "response.failed", StringComparison.Ordinal)
                    || string.Equals(type, "error", StringComparison.Ordinal))
                {
                    throw new HttpRequestException("OpenAI Responses stream reported failure.");
                }
            }
            catch (JsonException)
            {
                // dòng SSE hỏng -> bỏ qua, đọc tiếp
            }
        }

        var visible = text.ToString();
        return BuildReply(visible, inTok, outTok, systemPrompt, history, userMessage, reasoning.ToString());
    }

    // Gộp một chỗ duy nhất quyết định dùng usage thật hay ước lượng cục bộ.
    // Điều kiện là AND (cả hai đều 0): provider hợp lệ có thể trả output_tokens = 0 cho câu trả lời
    // rỗng — không được ghi đè số thật của provider.
    private ClaudeReply BuildReply(
        string visibleText,
        int inTok,
        int outTok,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        string reasoningSummary)
    {
        if (inTok != 0 || outTok != 0)
            return new ClaudeReply(visibleText, inTok, outTok, Cost(inTok, outTok), _config.Model);

        var estimatedIn = LlmTokenEstimator.CountPrompt(_config.Model, systemPrompt, history, userMessage);
        var estimatedOut = LlmTokenEstimator.CountText(_config.Model, visibleText)
            + LlmTokenEstimator.CountText(_config.Model, reasoningSummary);
        return new ClaudeReply(
            visibleText,
            estimatedIn,
            estimatedOut,
            Cost(estimatedIn, estimatedOut),
            _config.Model,
            IsEstimated: true);
    }

    // Giống OpenAiChatClient: không stream thật (usage phải chắc chắn cho cost cap) — resolve trọn
    // rồi phát 1 chunk nội dung + 1 chunk usage cuối.
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

    // Migrate mapping: messages -> input items; system -> instructions; max_tokens -> max_output_tokens.
    // Content part type: tin của user = input_text, tin assistant (history) = output_text.
    internal string BuildRequestBody(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage)
    {
        var input = new List<ResponsesInputItem>(history.Count + 1);
        foreach (var turn in history)
        {
            var isAssistant = turn.Role == "assistant";
            input.Add(new ResponsesInputItem(
                isAssistant ? "assistant" : "user",
                [new ResponsesContentPart(isAssistant ? "output_text" : "input_text", turn.Content)]));
        }
        input.Add(new ResponsesInputItem("user", [new ResponsesContentPart("input_text", userMessage)]));

        return JsonSerializer.Serialize(new ResponsesRequest(
            _config.Model,
            string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            input,
            _config.MaxOutputTokens,
            Stream: true), JsonOpts);
    }

    internal ClaudeReply ParseReply(
        string body,
        string systemPrompt = "",
        IReadOnlyList<ChatTurn>? history = null,
        string userMessage = "")
    {
        var parsed = JsonSerializer.Deserialize<ResponsesResponse>(body);
        var text = new StringBuilder();
        foreach (var item in parsed?.Output ?? [])
        {
            if (!string.Equals(item.Type, "message", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var part in item.Content ?? [])
            {
                if (string.Equals(part.Type, "output_text", StringComparison.OrdinalIgnoreCase))
                    text.Append(part.Text);
            }
        }

        var inTok = parsed?.Usage?.InputTokens ?? 0;
        var outTok = parsed?.Usage?.OutputTokens ?? 0;
        return BuildReply(text.ToString(), inTok, outTok, systemPrompt, history ?? [], userMessage, string.Empty);
    }

    private sealed record ResponsesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("instructions")] string? Instructions,
        [property: JsonPropertyName("input")] IReadOnlyList<ResponsesInputItem> Input,
        [property: JsonPropertyName("max_output_tokens")] int? MaxOutputTokens,
        [property: JsonPropertyName("stream")] bool Stream = true);

    private sealed record ResponsesInputItem(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<ResponsesContentPart> Content);

    private sealed record ResponsesContentPart(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record ResponsesResponse(
        [property: JsonPropertyName("output")] ResponsesOutputItem[]? Output,
        [property: JsonPropertyName("usage")] ResponsesUsage? Usage);

    private sealed record ResponsesOutputItem(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("content")] ResponsesContentPart[]? Content);

    private sealed record ResponsesUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}
