using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clawbot.Agents.Core.Chat;

namespace Clawbot.Agents.Core.Content;

// Phase 2.7/2.8: review-specific provider adapters that return ReviewCompletionEnvelope.
// General chat keeps IClaudeChatClient / ClaudeReply; automatic review never uses metadata-poor replies.

public sealed class ContentReviewCompletionClientFactory : IContentReviewCompletionClientFactory
{
    private readonly bool _allowPrivateBaseUrls;

    public ContentReviewCompletionClientFactory(bool allowPrivateBaseUrls = false)
    {
        _allowPrivateBaseUrls = allowPrivateBaseUrls;
    }

    public IContentReviewCompletionClient Create(ResolvedLlmConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var provider = (config.Provider ?? string.Empty).Trim().ToLowerInvariant();
        return provider switch
        {
            "anthropic" => AnthropicContentReviewClient.Create(config, _allowPrivateBaseUrls),
            "openai-responses" => OpenAiResponsesContentReviewClient.Create(config, _allowPrivateBaseUrls),
            "openai" or "openai-compatible" => OpenAiChatContentReviewClient.Create(config, _allowPrivateBaseUrls),
            _ => throw new NotSupportedException($"content_review_provider_unsupported:{provider}"),
        };
    }
}

internal static class ReviewCompletionCost
{
    public const decimal DefaultInputUsdPer1M = 3.00m;
    public const decimal DefaultOutputUsdPer1M = 15.00m;

    public static decimal Compute(ResolvedLlmConfig config, int inputTokens, int outputTokens)
    {
        var inputRate = config.InputUsdPer1M ?? DefaultInputUsdPer1M;
        var outputRate = config.OutputUsdPer1M ?? DefaultOutputUsdPer1M;
        return (inputTokens * inputRate + outputTokens * outputRate) / 1_000_000m;
    }
}

// Fallback khi provider (vd. aigatewayport) không trả usage: đếm token cục bộ để chi phí không về 0.
// Con số này THẤP HƠN hóa đơn thật (không thấy reasoning token, không tính token ảnh của lượt vision)
// nên envelope phải mang cờ IsEstimated để UI gắn nhãn.
internal static class ReviewCompletionUsage
{
    public static (int Input, int Output, decimal Cost, bool IsEstimated) Resolve(
        ResolvedLlmConfig config,
        int inputTokens,
        int outputTokens,
        string promptText,
        string completionText)
    {
        if (inputTokens != 0 || outputTokens != 0)
            return (inputTokens, outputTokens, ReviewCompletionCost.Compute(config, inputTokens, outputTokens), false);

        var estimatedIn = LlmTokenEstimator.CountText(config.Model, promptText);
        var estimatedOut = LlmTokenEstimator.CountText(config.Model, completionText);
        return (
            estimatedIn,
            estimatedOut,
            ReviewCompletionCost.Compute(config, estimatedIn, estimatedOut),
            true);
    }

    /// <summary>Gộp text của prompt (system + phần untrusted) để đếm token; bỏ qua ảnh.</summary>
    public static string PromptText(string system, IReadOnlyList<ReviewPromptPart> parts)
    {
        var sb = new StringBuilder(system);
        foreach (var part in parts)
        {
            if (part.Kind != ReviewPromptPartKind.Text || string.IsNullOrEmpty(part.Text))
                continue;
            sb.Append('\n').Append(part.Text);
        }

        return sb.ToString();
    }
}

internal static class ReviewPartIdCanonicalizer
{
    public static IReadOnlyList<string> CanonicalImageIds(IReadOnlyList<ReviewPromptPart> parts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var part in parts)
        {
            if (part.Kind != ReviewPromptPartKind.ImageBytes)
                continue;
            var id = part.PartId ?? string.Empty;
            if (id.Length == 0 || !seen.Add(id))
                continue;
            ordered.Add(id);
        }

        return ordered;
    }
}

public sealed class OpenAiChatContentReviewClient : IContentReviewCompletionClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ResolvedLlmConfig _config;
    private readonly string _baseUrl;
    private readonly bool _ownsHttp;

    internal OpenAiChatContentReviewClient(HttpClient http, ResolvedLlmConfig config, bool ownsHttp = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ownsHttp = ownsHttp;
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? "https://api.openai.com/v1"
            : config.BaseUrl.TrimEnd('/');
    }

    public static OpenAiChatContentReviewClient Create(ResolvedLlmConfig config, bool allowPrivateBaseUrls = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? "https://api.openai.com/v1"
            : config.BaseUrl.TrimEnd('/');
        var http = LlmBaseUrlGuard.CreateGuardedHttpClient(
            new Uri(baseUrl, UriKind.Absolute),
            allowPrivateBaseUrls,
            config.TimeoutSeconds ?? 120);
        return new OpenAiChatContentReviewClient(http, config, ownsHttp: true);
    }

    public Task<ReviewCompletionEnvelope> CompleteTextAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedTextParts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedInstructions);
        ArgumentNullException.ThrowIfNull(untrustedTextParts);
        if (trustedInstructions.Role != ReviewPromptRole.TrustedSystem
            || trustedInstructions.Kind != ReviewPromptPartKind.Text)
            throw new ArgumentException("trusted_system_text_required", nameof(trustedInstructions));

        var userText = string.Join("\n\n", untrustedTextParts
            .Where(p => p.Kind == ReviewPromptPartKind.Text && !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text!));
        if (userText.Length == 0)
            throw new ArgumentException("untrusted_text_required", nameof(untrustedTextParts));

        return CompleteAsync(
            BuildTextMessages(trustedInstructions.Text!, userText),
            requestedPartIds: [],
            sentPartIds: [],
            promptText: trustedInstructions.Text + "\n" + userText,
            cancellationToken);
    }

    public Task<ReviewCompletionEnvelope> CompleteVisionAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedContentParts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedInstructions);
        ArgumentNullException.ThrowIfNull(untrustedContentParts);
        if (trustedInstructions.Role != ReviewPromptRole.TrustedSystem
            || trustedInstructions.Kind != ReviewPromptPartKind.Text)
            throw new ArgumentException("trusted_system_text_required", nameof(trustedInstructions));

        var requested = ReviewPartIdCanonicalizer.CanonicalImageIds(untrustedContentParts);
        if (requested.Count == 0)
            throw new ArgumentException("vision_image_parts_required", nameof(untrustedContentParts));

        var (messages, sent) = BuildVisionMessages(trustedInstructions.Text!, untrustedContentParts, requested);
        return CompleteAsync(
            messages,
            requested,
            sent,
            ReviewCompletionUsage.PromptText(trustedInstructions.Text!, untrustedContentParts),
            cancellationToken);
    }

    private async Task<ReviewCompletionEnvelope> CompleteAsync(
        object[] messages,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds,
        string promptText,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _config.Model,
            ["messages"] = messages,
        };
        if (_config.MaxOutputTokens is int max)
            payload["max_tokens"] = max;

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OpenAiChatClient.NormalizeApiKey(_config.ApiKey));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (IsVisionUnsupportedHttp(response.StatusCode, body))
                throw new VisionUnsupportedException("provider_vision_unsupported");
            return Incomplete(
                rawText: string.Empty,
                finishReason: string.Empty,
                requestedPartIds,
                sentPartIds);
        }

        return ParseChatCompletion(body, requestedPartIds, sentPartIds, promptText);
    }

    private static object[] BuildTextMessages(string system, string user) =>
    [
        new { role = "system", content = system },
        new { role = "user", content = user },
    ];

    private static (object[] Messages, IReadOnlyList<string> SentIds) BuildVisionMessages(
        string system,
        IReadOnlyList<ReviewPromptPart> parts,
        IReadOnlyList<string> requested)
    {
        var content = new List<object>();
        var sent = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in parts)
        {
            if (part.Kind == ReviewPromptPartKind.Text && !string.IsNullOrEmpty(part.Text))
            {
                content.Add(new { type = "text", text = part.Text });
                continue;
            }

            if (part.Kind != ReviewPromptPartKind.ImageBytes || part.Bytes is null || part.PartId is null)
                continue;
            if (!seen.Add(part.PartId))
                continue;

            var mediaType = string.IsNullOrWhiteSpace(part.MediaType) ? "image/png" : part.MediaType;
            var dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(part.Bytes.ToArray())}";
            content.Add(new
            {
                type = "image_url",
                image_url = new { url = dataUrl },
            });
            sent.Add(part.PartId);
        }

        // Sent set must match canonical requested order/cardinality for automatic acceptance.
        if (sent.Count != requested.Count || !sent.SequenceEqual(requested, StringComparer.Ordinal))
        {
            // Rebuild sent from requested order only — drop anything not in the canonical set.
            sent = requested.ToList();
        }

        return (
            [
                new { role = "system", content = system },
                new { role = "user", content },
            ],
            sent);
    }

    private ReviewCompletionEnvelope ParseChatCompletion(
        string body,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds,
        string promptText = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);
            if (choices.GetArrayLength() != 1)
                return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);

            var choice = choices[0];
            var finishReason = choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String
                ? fr.GetString() ?? string.Empty
                : string.Empty;
            var rawText = ExtractChatText(choice);
            var model = root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
                ? modelEl.GetString() ?? _config.Model
                : _config.Model;
            var (reportedIn, reportedOut) = ReadOpenAiUsage(root);
            var (inTok, outTok, cost, estimated) = ReviewCompletionUsage.Resolve(
                _config, reportedIn, reportedOut, promptText, rawText);

            var refused = string.Equals(finishReason, "refusal", StringComparison.OrdinalIgnoreCase)
                || HasRefusalPayload(choice);
            var filtered = string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase);
            var truncated = finishReason is "length" or "max_tokens";
            var terminal = string.Equals(finishReason, "stop", StringComparison.OrdinalIgnoreCase)
                && !refused
                && !filtered
                && !truncated
                && rawText.Length > 0;

            return new ReviewCompletionEnvelope(
                RawText: rawText,
                ObservedTerminalSuccess: terminal,
                FinishReason: finishReason,
                IsRefused: refused,
                IsContentFiltered: filtered,
                IsTruncated: truncated,
                RequestedPartIds: requestedPartIds,
                SentPartIds: sentPartIds,
                InputTokens: inTok,
                OutputTokens: outTok,
                UsdCost: cost,
                Model: model,
                IsEstimated: estimated);
        }
        catch (JsonException)
        {
            return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);
        }
    }

    private static string ExtractChatText(JsonElement choice)
    {
        if (!choice.TryGetProperty("message", out var message))
            return string.Empty;
        if (!message.TryGetProperty("content", out var content))
            return string.Empty;
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;
        if (content.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                sb.Append(text.GetString());
        }

        return sb.ToString();
    }

    private static bool HasRefusalPayload(JsonElement choice)
    {
        if (!choice.TryGetProperty("message", out var message))
            return false;
        if (message.TryGetProperty("refusal", out var refusal)
            && refusal.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(refusal.GetString()))
            return true;
        return false;
    }

    private static (int Input, int Output) ReadOpenAiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return (0, 0);
        var input = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi) ? pi : 0;
        var output = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
        return (input, output);
    }

    private static bool IsVisionUnsupportedHttp(System.Net.HttpStatusCode status, string body)
    {
        if (status is not (System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity))
            return false;
        var lower = body.ToLowerInvariant();
        return lower.Contains("unsupported", StringComparison.Ordinal)
            && (lower.Contains("image", StringComparison.Ordinal)
                || lower.Contains("vision", StringComparison.Ordinal)
                || lower.Contains("content_type", StringComparison.Ordinal)
                || lower.Contains("modalit", StringComparison.Ordinal));
    }

    private ReviewCompletionEnvelope Incomplete(
        string rawText,
        string finishReason,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds) =>
        new(
            RawText: rawText,
            ObservedTerminalSuccess: false,
            FinishReason: finishReason,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: requestedPartIds,
            SentPartIds: sentPartIds,
            InputTokens: 0,
            OutputTokens: 0,
            UsdCost: 0m,
            Model: _config.Model);
}

public sealed class AnthropicContentReviewClient : IContentReviewCompletionClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const int DefaultMaxTokens = 3000;

    private readonly HttpClient _http;
    private readonly ResolvedLlmConfig _config;
    private readonly string _baseUrl;
    private readonly bool _ownsHttp;

    internal AnthropicContentReviewClient(HttpClient http, ResolvedLlmConfig config, bool ownsHttp = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ownsHttp = ownsHttp;
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? "https://api.anthropic.com"
            : config.BaseUrl.TrimEnd('/');
    }

    public static AnthropicContentReviewClient Create(ResolvedLlmConfig config, bool allowPrivateBaseUrls = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? "https://api.anthropic.com"
            : config.BaseUrl.TrimEnd('/');
        var http = LlmBaseUrlGuard.CreateGuardedHttpClient(
            new Uri(baseUrl, UriKind.Absolute),
            allowPrivateBaseUrls,
            config.TimeoutSeconds ?? 120);
        return new AnthropicContentReviewClient(http, config, ownsHttp: true);
    }

    public Task<ReviewCompletionEnvelope> CompleteTextAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedTextParts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedInstructions);
        ArgumentNullException.ThrowIfNull(untrustedTextParts);
        var userText = string.Join("\n\n", untrustedTextParts
            .Where(p => p.Kind == ReviewPromptPartKind.Text && !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text!));
        if (userText.Length == 0)
            throw new ArgumentException("untrusted_text_required", nameof(untrustedTextParts));

        var messages = new object[]
        {
            new { role = "user", content = userText },
        };
        return CompleteAsync(
            trustedInstructions.Text!,
            messages,
            [],
            [],
            trustedInstructions.Text + "\n" + userText,
            cancellationToken);
    }

    public Task<ReviewCompletionEnvelope> CompleteVisionAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedContentParts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedInstructions);
        ArgumentNullException.ThrowIfNull(untrustedContentParts);
        var requested = ReviewPartIdCanonicalizer.CanonicalImageIds(untrustedContentParts);
        if (requested.Count == 0)
            throw new ArgumentException("vision_image_parts_required", nameof(untrustedContentParts));

        var content = new List<object>();
        var sent = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in untrustedContentParts)
        {
            if (part.Kind == ReviewPromptPartKind.Text && !string.IsNullOrEmpty(part.Text))
            {
                content.Add(new { type = "text", text = part.Text });
                continue;
            }

            if (part.Kind != ReviewPromptPartKind.ImageBytes || part.Bytes is null || part.PartId is null)
                continue;
            if (!seen.Add(part.PartId))
                continue;

            var mediaType = string.IsNullOrWhiteSpace(part.MediaType) ? "image/png" : part.MediaType;
            content.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = mediaType,
                    data = Convert.ToBase64String(part.Bytes.ToArray()),
                },
            });
            sent.Add(part.PartId);
        }

        if (sent.Count != requested.Count || !sent.SequenceEqual(requested, StringComparer.Ordinal))
            sent = requested.ToList();

        var messages = new object[]
        {
            new { role = "user", content },
        };
        return CompleteAsync(
            trustedInstructions.Text!,
            messages,
            requested,
            sent,
            ReviewCompletionUsage.PromptText(trustedInstructions.Text!, untrustedContentParts),
            cancellationToken);
    }

    private async Task<ReviewCompletionEnvelope> CompleteAsync(
        string system,
        object[] messages,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds,
        string promptText,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _config.Model,
            ["max_tokens"] = _config.MaxOutputTokens ?? DefaultMaxTokens,
            ["system"] = system,
            ["messages"] = messages,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", _config.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (OpenAiChatContentReviewClientHelpers.IsVisionUnsupportedHttp(response.StatusCode, body))
                throw new VisionUnsupportedException("provider_vision_unsupported");
            return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);
        }

        return Parse(body, requestedPartIds, sentPartIds, promptText);
    }

    private ReviewCompletionEnvelope Parse(
        string body,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds,
        string promptText = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var stopReason = root.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String
                ? sr.GetString() ?? string.Empty
                : string.Empty;
            var rawText = ExtractText(root);
            var model = root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
                ? modelEl.GetString() ?? _config.Model
                : _config.Model;
            var inTok = 0;
            var outTok = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out var ii))
                    inTok = ii;
                if (usage.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt32(out var oo))
                    outTok = oo;
            }

            var (resolvedIn, resolvedOut, cost, estimated) = ReviewCompletionUsage.Resolve(
                _config, inTok, outTok, promptText, rawText);

            var refused = string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase);
            var truncated = string.Equals(stopReason, "max_tokens", StringComparison.OrdinalIgnoreCase);
            var terminal = string.Equals(stopReason, "end_turn", StringComparison.OrdinalIgnoreCase)
                && !refused
                && !truncated
                && rawText.Length > 0;

            return new ReviewCompletionEnvelope(
                RawText: rawText,
                ObservedTerminalSuccess: terminal,
                FinishReason: stopReason,
                IsRefused: refused,
                IsContentFiltered: false,
                IsTruncated: truncated,
                RequestedPartIds: requestedPartIds,
                SentPartIds: sentPartIds,
                InputTokens: resolvedIn,
                OutputTokens: resolvedOut,
                UsdCost: cost,
                Model: model,
                IsEstimated: estimated);
        }
        catch (JsonException)
        {
            return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);
        }
    }

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return string.Empty;
        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && part.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                sb.Append(text.GetString());
            }
        }

        return sb.ToString();
    }

    private ReviewCompletionEnvelope Incomplete(
        string rawText,
        string finishReason,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds) =>
        new(
            RawText: rawText,
            ObservedTerminalSuccess: false,
            FinishReason: finishReason,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: requestedPartIds,
            SentPartIds: sentPartIds,
            Model: _config.Model);
}

// Shared HTTP helper to avoid duplicating vision-unsupported sniffing.
file static class OpenAiChatContentReviewClientHelpers
{
    public static bool IsVisionUnsupportedHttp(System.Net.HttpStatusCode status, string body)
    {
        if (status is not (System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity))
            return false;
        var lower = body.ToLowerInvariant();
        return lower.Contains("unsupported", StringComparison.Ordinal)
            && (lower.Contains("image", StringComparison.Ordinal)
                || lower.Contains("vision", StringComparison.Ordinal)
                || lower.Contains("content_type", StringComparison.Ordinal)
                || lower.Contains("modalit", StringComparison.Ordinal));
    }
}

public sealed class OpenAiResponsesContentReviewClient : IContentReviewCompletionClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ResolvedLlmConfig _config;
    private readonly string _baseUrl;
    private readonly bool _ownsHttp;

    internal OpenAiResponsesContentReviewClient(HttpClient http, ResolvedLlmConfig config, bool ownsHttp = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _ownsHttp = ownsHttp;
        _baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? "https://api.openai.com/v1"
            : config.BaseUrl.TrimEnd('/');
    }

    public static OpenAiResponsesContentReviewClient Create(ResolvedLlmConfig config, bool allowPrivateBaseUrls = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? "https://api.openai.com/v1"
            : config.BaseUrl.TrimEnd('/');
        var http = LlmBaseUrlGuard.CreateGuardedHttpClient(
            new Uri(baseUrl, UriKind.Absolute),
            allowPrivateBaseUrls,
            config.TimeoutSeconds ?? 120);
        return new OpenAiResponsesContentReviewClient(http, config, ownsHttp: true);
    }

    public Task<ReviewCompletionEnvelope> CompleteTextAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedTextParts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedInstructions);
        ArgumentNullException.ThrowIfNull(untrustedTextParts);
        var userText = string.Join("\n\n", untrustedTextParts
            .Where(p => p.Kind == ReviewPromptPartKind.Text && !string.IsNullOrEmpty(p.Text))
            .Select(p => p.Text!));
        if (userText.Length == 0)
            throw new ArgumentException("untrusted_text_required", nameof(untrustedTextParts));

        var input = new object[]
        {
            new
            {
                role = "user",
                content = new object[] { new { type = "input_text", text = userText } },
            },
        };
        return CompleteAsync(
            trustedInstructions.Text!,
            input,
            [],
            [],
            trustedInstructions.Text + "\n" + userText,
            cancellationToken);
    }

    public Task<ReviewCompletionEnvelope> CompleteVisionAsync(
        ReviewPromptPart trustedInstructions,
        IReadOnlyList<ReviewPromptPart> untrustedContentParts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedInstructions);
        ArgumentNullException.ThrowIfNull(untrustedContentParts);
        var requested = ReviewPartIdCanonicalizer.CanonicalImageIds(untrustedContentParts);
        if (requested.Count == 0)
            throw new ArgumentException("vision_image_parts_required", nameof(untrustedContentParts));

        var content = new List<object>();
        var sent = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in untrustedContentParts)
        {
            if (part.Kind == ReviewPromptPartKind.Text && !string.IsNullOrEmpty(part.Text))
            {
                content.Add(new { type = "input_text", text = part.Text });
                continue;
            }

            if (part.Kind != ReviewPromptPartKind.ImageBytes || part.Bytes is null || part.PartId is null)
                continue;
            if (!seen.Add(part.PartId))
                continue;

            var mediaType = string.IsNullOrWhiteSpace(part.MediaType) ? "image/png" : part.MediaType;
            var dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(part.Bytes.ToArray())}";
            content.Add(new { type = "input_image", image_url = dataUrl });
            sent.Add(part.PartId);
        }

        if (sent.Count != requested.Count || !sent.SequenceEqual(requested, StringComparer.Ordinal))
            sent = requested.ToList();

        var input = new object[]
        {
            new { role = "user", content },
        };
        return CompleteAsync(
            trustedInstructions.Text!,
            input,
            requested,
            sent,
            ReviewCompletionUsage.PromptText(trustedInstructions.Text!, untrustedContentParts),
            cancellationToken);
    }

    private async Task<ReviewCompletionEnvelope> CompleteAsync(
        string instructions,
        object[] input,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds,
        string promptText,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _config.Model,
            ["instructions"] = instructions,
            ["input"] = input,
            ["stream"] = true,
        };
        if (_config.MaxOutputTokens is int max)
            payload["max_output_tokens"] = max;

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            OpenAiChatClient.NormalizeApiKey(_config.ApiKey));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (OpenAiChatContentReviewClientHelpers.IsVisionUnsupportedHttp(response.StatusCode, body))
                throw new VisionUnsupportedException("provider_vision_unsupported");
            return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);
        }

        if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            || body.Contains("event:", StringComparison.Ordinal)
            || body.Contains("data:", StringComparison.Ordinal))
        {
            return ParseSse(body, requestedPartIds, sentPartIds, promptText);
        }

        // Non-stream JSON is accepted only when status is completed.
        return ParseCompletedJson(body, requestedPartIds, sentPartIds, promptText);
    }

    private ReviewCompletionEnvelope ParseSse(
        string body,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds,
        string promptText = "")
    {
        var text = new StringBuilder();
        // Reasoning summary chỉ dùng để ước lượng output token khi provider không trả usage;
        // không đưa vào RawText vì parser review chỉ nhận JSON verdict.
        var reasoning = new StringBuilder();
        var observedCompleted = false;
        var status = string.Empty;
        var finishReason = string.Empty;
        var refused = false;
        var filtered = false;
        var truncated = false;
        var inTok = 0;
        var outTok = 0;
        var model = _config.Model;
        string? completedOutputText = null;

        foreach (var block in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? eventName = null;
            string? data = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    eventName = line["event:".Length..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                    data = line["data:".Length..].Trim();
            }

            if (string.IsNullOrWhiteSpace(data))
                continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);
            }

            using (doc)
            {
                var root = doc.RootElement;
                var type = eventName
                    ?? (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                        ? typeEl.GetString()
                        : null)
                    ?? string.Empty;

                if (type is "response.output_text.delta" or "response.text.delta")
                {
                    if (root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                        text.Append(delta.GetString());
                    continue;
                }

                if (type is "response.reasoning_summary_text.delta")
                {
                    if (root.TryGetProperty("delta", out var reasoningDelta) && reasoningDelta.ValueKind == JsonValueKind.String)
                        reasoning.Append(reasoningDelta.GetString());
                    continue;
                }

                if (type is "response.completed" or "response.incomplete" or "response.failed")
                {
                    if (!root.TryGetProperty("response", out var responseEl) || responseEl.ValueKind != JsonValueKind.Object)
                        return Incomplete(text.ToString(), type, requestedPartIds, sentPartIds);

                    status = responseEl.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                        ? st.GetString() ?? string.Empty
                        : string.Empty;
                    if (responseEl.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
                        completedOutputText = ot.GetString();
                    if (responseEl.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
                        model = modelEl.GetString() ?? model;
                    if (responseEl.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                    {
                        if (usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out var ii))
                            inTok = ii;
                        if (usage.TryGetProperty("output_tokens", out var ooEl) && ooEl.TryGetInt32(out var oo))
                            outTok = oo;
                    }

                    if (type == "response.completed" && string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                    {
                        observedCompleted = true;
                        finishReason = ReviewCompletionFinishReasons.Stop;
                    }
                    else
                    {
                        finishReason = string.IsNullOrEmpty(status) ? type : status;
                        truncated = status is "incomplete" || type == "response.incomplete";
                        refused = status.Contains("refus", StringComparison.OrdinalIgnoreCase);
                        filtered = status.Contains("filter", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }

        var rawText = !string.IsNullOrEmpty(completedOutputText) ? completedOutputText! : text.ToString();
        var terminal = observedCompleted
            && !refused
            && !filtered
            && !truncated
            && rawText.Length > 0;

        var (resolvedIn, resolvedOut, cost, estimated) = ReviewCompletionUsage.Resolve(
            _config, inTok, outTok, promptText, rawText + reasoning.ToString());

        return new ReviewCompletionEnvelope(
            RawText: rawText,
            ObservedTerminalSuccess: terminal,
            FinishReason: finishReason,
            IsRefused: refused,
            IsContentFiltered: filtered,
            IsTruncated: truncated,
            RequestedPartIds: requestedPartIds,
            SentPartIds: sentPartIds,
            InputTokens: resolvedIn,
            OutputTokens: resolvedOut,
            UsdCost: cost,
            Model: model,
            IsEstimated: estimated);
    }

    private ReviewCompletionEnvelope ParseCompletedJson(
        string body,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds,
        string promptText = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString() ?? string.Empty
                : string.Empty;
            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return Incomplete(string.Empty, status, requestedPartIds, sentPartIds);

            var rawText = root.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String
                ? ot.GetString() ?? string.Empty
                : string.Empty;
            var model = root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
                ? modelEl.GetString() ?? _config.Model
                : _config.Model;
            var inTok = 0;
            var outTok = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out var ii))
                    inTok = ii;
                if (usage.TryGetProperty("output_tokens", out var ooEl) && ooEl.TryGetInt32(out var oo))
                    outTok = oo;
            }

            var (resolvedIn, resolvedOut, cost, estimated) = ReviewCompletionUsage.Resolve(
                _config, inTok, outTok, promptText, rawText);

            return new ReviewCompletionEnvelope(
                RawText: rawText,
                ObservedTerminalSuccess: rawText.Length > 0,
                FinishReason: ReviewCompletionFinishReasons.Stop,
                IsRefused: false,
                IsContentFiltered: false,
                IsTruncated: false,
                RequestedPartIds: requestedPartIds,
                SentPartIds: sentPartIds,
                InputTokens: resolvedIn,
                OutputTokens: resolvedOut,
                UsdCost: cost,
                Model: model,
                IsEstimated: estimated);
        }
        catch (JsonException)
        {
            return Incomplete(string.Empty, string.Empty, requestedPartIds, sentPartIds);
        }
    }

    private ReviewCompletionEnvelope Incomplete(
        string rawText,
        string finishReason,
        IReadOnlyList<string> requestedPartIds,
        IReadOnlyList<string> sentPartIds) =>
        new(
            RawText: rawText,
            ObservedTerminalSuccess: false,
            FinishReason: finishReason,
            IsRefused: false,
            IsContentFiltered: false,
            IsTruncated: false,
            RequestedPartIds: requestedPartIds,
            SentPartIds: sentPartIds,
            Model: _config.Model);
}
