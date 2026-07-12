using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Infrastructure.Integrations.Meta;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Content.Publishing;

// Facebook publishing uses the tenant Meta OAuth connection and its encrypted per-Page token.
// Zalo keeps the encrypted credential resolver; legacy Facebook options remain fallback-only.
public sealed class GraphPublisherOptions
{
    public const string SectionName = "Content:GraphPublisher";

    public GraphChannelOptions Facebook { get; init; } = new();
    public GraphChannelOptions Zalo { get; init; } = new();
}

public sealed class GraphChannelOptions
{
    public bool Enabled { get; init; }
    public string Endpoint { get; init; } = string.Empty;
    public string PageAccessToken { get; init; } = string.Empty;
    public string PageId { get; init; } = string.Empty;
    // Zalo-specific; FB ignores these.
    public string OaAccessToken { get; init; } = string.Empty;
    public string OaId { get; init; } = string.Empty;
}

public sealed partial class GraphSocialPublisher(
    HttpClient http,
    IOptions<GraphPublisherOptions> options,
    ISocialCredentialResolver? credentialResolver = null,
    ILogger<GraphSocialPublisher>? logger = null,
    IMetaIntegrationService? metaIntegration = null,
    IMetaGraphClient? metaGraph = null) : ISocialPublisher
{
    private const string FacebookEndpoint = "https://graph.facebook.com/v25.0";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = http;
    private readonly GraphPublisherOptions _options = options.Value;
    private readonly ISocialCredentialResolver? _credentialResolver = credentialResolver;
    private readonly ILogger<GraphSocialPublisher> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GraphSocialPublisher>.Instance;
    private readonly IMetaIntegrationService? _metaIntegration = metaIntegration;
    private readonly IMetaGraphClient? _metaGraph = metaGraph;

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var platform = NormalizePlatform(request.Platform);
        return platform switch
        {
            "facebook" => await PublishFacebookAsync(request, ct).ConfigureAwait(false),
            "zalo" => await PublishZaloAsync(request, ct).ConfigureAwait(false),
            _ => new PublishResult(false, null, $"unsupported_platform:{request.Platform}"),
        };
    }

    // EARS[WHEN publishing to Facebook THE SYSTEM SHALL resolve credentials from the encrypted DB store first,
    // falling back to options only when no DB credential exists, so prod creds never live in appsettings.json]
    private async Task<PublishResult> PublishFacebookAsync(PublishRequest request, CancellationToken ct)
    {
        if (_metaIntegration is not null && _metaGraph is not null)
        {
            var page = await _metaIntegration.ResolvePageAsync(request.TenantId, request.MetaAssetId, ct).ConfigureAwait(false);
            if (page is not null)
                return await PublishFacebookWithMetaAsync(request, page, ct).ConfigureAwait(false);
        }

        var fb = await ResolveChannelAsync(request.TenantId, "facebook", ct).ConfigureAwait(false);
        if (fb is null || !fb.Enabled || string.IsNullOrWhiteSpace(fb.PageAccessToken) || string.IsNullOrWhiteSpace(fb.PageId))
            return new PublishResult(false, null, "facebook_not_configured");

        var imageUrl = FirstImageUrl(request.AssetsJson);
        var path = imageUrl is null ? "feed" : "photos";
        var url = $"{FacebookEndpoint}/{Uri.EscapeDataString(fb.PageId)}/{path}";
        var fields = imageUrl is null
            ? new Dictionary<string, string>
            {
                ["message"] = request.Body,
                ["access_token"] = fb.PageAccessToken,
            }
            : new Dictionary<string, string>
            {
                ["caption"] = request.Body,
                ["url"] = imageUrl,
                ["access_token"] = fb.PageAccessToken,
            };
        using var form = new FormUrlEncodedContent(fields);

        try
        {
            using var resp = await _http.PostAsync(url, form, ct).ConfigureAwait(false);
            var body = resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new PublishResult(false, null, $"facebook_http_{(int)resp.StatusCode}:{Truncate(body)}");

            using var doc = JsonDocument.Parse(body);
            var idEl = doc.RootElement.TryGetProperty("post_id", out var postIdEl)
                ? postIdEl
                : (doc.RootElement.TryGetProperty("id", out var fallbackId) ? fallbackId : default);
            if (idEl.ValueKind != JsonValueKind.String)
                return new PublishResult(false, null, "facebook_response_missing_id");
            var postId = idEl.GetString()!;
            var postUrl = $"https://www.facebook.com/{postId}";
            LogPublished(_logger, "facebook", request.TenantId, request.ContentItemId, postUrl);
            return new PublishResult(true, postUrl, null);
        }
        catch (HttpRequestException ex) { return new PublishResult(false, null, ex.Message); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new PublishResult(false, null, "facebook_timeout"); }
        catch (JsonException ex) { return new PublishResult(false, null, $"facebook_parse:{ex.Message}"); }
    }

    private async Task<PublishResult> PublishFacebookWithMetaAsync(
        PublishRequest request,
        MetaPageCredential page,
        CancellationToken ct)
    {
        var imageUrl = FirstImageUrl(request.AssetsJson);
        try
        {
            var published = await _metaGraph!.PublishPageAsync(
                request.TenantId,
                page.PageId,
                page.PageAccessToken,
                request.Body,
                imageUrl,
                ct).ConfigureAwait(false);
            LogPublished(_logger, "facebook", request.TenantId, request.ContentItemId, published.Permalink);
            return new PublishResult(true, published.Permalink, null);
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            try
            {
                var refreshed = await _metaIntegration!.RefreshPageAsync(request.TenantId, request.MetaAssetId, ct).ConfigureAwait(false);
                if (refreshed is null)
                {
                    await _metaIntegration.MarkReconnectRequiredAsync(request.TenantId, "meta_page_token_refresh_failed", ct).ConfigureAwait(false);
                    return new PublishResult(false, null, "facebook_reconnect_required");
                }

                var published = await _metaGraph!.PublishPageAsync(
                    request.TenantId,
                    refreshed.PageId,
                    refreshed.PageAccessToken,
                    request.Body,
                    imageUrl,
                    ct).ConfigureAwait(false);
                LogPublished(_logger, "facebook", request.TenantId, request.ContentItemId, published.Permalink);
                return new PublishResult(true, published.Permalink, null);
            }
            catch (MetaGraphException refreshException) when (refreshException.IsTokenError)
            {
                await _metaIntegration!.MarkReconnectRequiredAsync(
                    request.TenantId,
                    $"meta_token_{refreshException.Code}_{refreshException.Subcode}",
                    ct).ConfigureAwait(false);
                return new PublishResult(false, null, "facebook_reconnect_required");
            }
            catch (MetaGraphException refreshException)
            {
                LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, refreshException.Message, refreshException);
                return new PublishResult(false, null, $"facebook_graph_{refreshException.Code ?? refreshException.HttpStatus ?? 0}");
            }
            catch (HttpRequestException refreshException)
            {
                LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, refreshException.Message, refreshException);
                return new PublishResult(false, null, "facebook_unavailable");
            }
        }
        catch (MetaGraphException ex)
        {
            LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, ex.Message, ex);
            return new PublishResult(false, null, $"facebook_graph_{ex.Code ?? ex.HttpStatus ?? 0}");
        }
        catch (HttpRequestException ex)
        {
            LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, ex.Message, ex);
            return new PublishResult(false, null, "facebook_unavailable");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new PublishResult(false, null, "facebook_timeout");
        }
    }

    // EARS[WHEN publishing to Zalo THE SYSTEM SHALL resolve credentials from the encrypted DB store first, falling
    // back to options when none exists]
    private async Task<PublishResult> PublishZaloAsync(PublishRequest request, CancellationToken ct)
    {
        var z = await ResolveChannelAsync(request.TenantId, "zalo", ct).ConfigureAwait(false);
        if (z is null || !z.Enabled || string.IsNullOrWhiteSpace(z.Endpoint) || string.IsNullOrWhiteSpace(z.OaAccessToken) || string.IsNullOrWhiteSpace(z.OaId))
            return new PublishResult(false, null, "zalo_not_configured");

        var url = $"{z.Endpoint.TrimEnd('/')}/article/verify_only";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                @access_token = z.OaAccessToken,
                @type = "normal",
                body = request.Body,
            }, options: JsonOpts),
        };

        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new PublishResult(false, null, $"zalo_http_{(int)resp.StatusCode}:{Truncate(body)}");

            using var doc = JsonDocument.Parse(body);
            // Zalo OA returns { error, message, data: { token } } where the token becomes the post id.
            if (doc.RootElement.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Number && errEl.GetInt32() != 0)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "zalo_error";
                return new PublishResult(false, null, $"zalo_error:{msg}");
            }
            string? postToken = null;
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var tk) && tk.ValueKind == JsonValueKind.String)
                postToken = tk.GetString();
            var postUrl = postToken is null ? null : $"https://zalo.me/p/{postToken}";
            LogPublished(_logger, "zalo", request.TenantId, request.ContentItemId, postUrl ?? "zalo_post");
            return new PublishResult(true, postUrl, null);
        }
        catch (HttpRequestException ex) { return new PublishResult(false, null, ex.Message); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new PublishResult(false, null, "zalo_timeout"); }
        catch (JsonException ex) { return new PublishResult(false, null, $"zalo_parse:{ex.Message}"); }
    }

    // SPEC-16 Module M-1: DB credential store first (encrypted); options fallback for dev/single-tenant.
    private async Task<GraphChannelOptions?> ResolveChannelAsync(Guid tenantId, string provider, CancellationToken ct)
    {
        if (_credentialResolver is not null)
        {
            var dbCreds = await _credentialResolver.ResolveAsync(tenantId, provider, ct).ConfigureAwait(false);
            if (dbCreds is not null) return dbCreds;
        }
        return provider == "facebook" ? _options.Facebook : (provider == "zalo" ? _options.Zalo : null);
    }

    private static string? FirstImageUrl(string assetsJson)
    {
        if (string.IsNullOrWhiteSpace(assetsJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(assetsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var asset in doc.RootElement.EnumerateArray())
            {
                var type = asset.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "image";
                if (!string.Equals(type, "image", StringComparison.OrdinalIgnoreCase)) continue;
                if (asset.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                {
                    var url = urlEl.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                        return url;
                }
            }
        }
        catch (JsonException) { return null; }

        return null;
    }

    private static string NormalizePlatform(string platform) =>
        (platform ?? string.Empty).Trim().ToLowerInvariant();

    private static string Truncate(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length > 200 ? s[..200] : s);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Information, Message = "GraphSocialPublisher published {platform} content {contentItemId} tenant {tenantId}: {postUrl}")]
    private static partial void LogPublished(ILogger logger, string platform, Guid tenantId, Guid contentItemId, string postUrl);

    [LoggerMessage(EventId = 5204, Level = LogLevel.Warning, Message = "Meta publish failed for content {ContentItemId} tenant {TenantId}: {Reason}")]
    private static partial void LogMetaPublishFailed(ILogger logger, Guid tenantId, Guid contentItemId, string reason, Exception exception);
}
