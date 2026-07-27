using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Content.Publishing;

public sealed record PublishRequest(
    Guid TenantId,
    Guid ContentItemId,
    string Platform,
    string Body,
    string AssetsJson,
    DateTimeOffset ScheduledAt,
    Guid? MetaAssetId = null,
    string? ProviderTargetId = null);

public sealed record PublishResult(
    bool Success,
    string? PostUrl,
    string? Error,
    string? ExternalId = null);

public sealed class PublisherOptions
{
    public const string SectionName = "Content:Publisher";

    public string Endpoint { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public interface ISocialPublisher
{
    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct = default);
}

public sealed class HttpSocialPublisher(HttpClient http, IOptions<PublisherOptions> options) : ISocialPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = http;
    private readonly PublisherOptions _options = options.Value;

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.Token))
            return new PublishResult(false, null, "publisher_not_configured");

        // Phase 2.14: operator endpoint only — HTTPS/no userinfo/no private SSRF unless ops allowlist.
        if (!Clawbot.Agents.Core.Chat.LlmBaseUrlGuard.IsAllowedBaseUrl(_options.Endpoint))
            return new PublishResult(false, null, "publisher_endpoint_not_allowed");

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.Endpoint, UriKind.Absolute))
        {
            Content = new StringContent(BuildPayload(request), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);

        try
        {
            using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
            var responseBody = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = string.IsNullOrWhiteSpace(responseBody)
                    ? $"publisher_http_{(int)response.StatusCode}"
                    : responseBody;
                return new PublishResult(false, null, error);
            }

            var postUrl = ExtractPostUrl(responseBody) ?? response.Headers.Location?.ToString();
            return new PublishResult(true, postUrl, null);
        }
        catch (HttpRequestException ex)
        {
            return new PublishResult(false, null, ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new PublishResult(false, null, "publisher_timeout");
        }
    }

    internal static string BuildPayload(PublishRequest request)
    {
        var root = new JsonObject
        {
            ["profile_ids"] = new JsonArray(CleanLine(request.Platform)),
            ["text"] = request.Body,
            ["scheduled_at"] = request.ScheduledAt.ToUnixTimeSeconds(),
            ["metadata"] = new JsonObject
            {
                ["tenant_id"] = request.TenantId.ToString(),
                ["content_item_id"] = request.ContentItemId.ToString(),
            },
            ["media"] = ParseAssets(request.AssetsJson),
        };

        return root.ToJsonString(JsonOptions);
    }

    private static JsonNode ParseAssets(string assetsJson)
    {
        if (string.IsNullOrWhiteSpace(assetsJson))
            return new JsonArray();

        try
        {
            return JsonNode.Parse(assetsJson) ?? new JsonArray();
        }
        catch (JsonException)
        {
            return new JsonArray();
        }
    }

    private static string? ExtractPostUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            foreach (var property in new[] { "post_url", "url", "permalink", "update_url" })
            {
                if (doc.RootElement.TryGetProperty(property, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string CleanLine(string value) =>
        value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}
