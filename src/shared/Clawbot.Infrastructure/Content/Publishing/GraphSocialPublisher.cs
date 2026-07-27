using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clawbot.Infrastructure.Integrations.Meta;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Clawbot.Infrastructure.Content.Publishing;

// Facebook publishing uses the tenant Meta OAuth connection and its encrypted per-Page token.
// Zalo keeps the encrypted credential resolver; legacy Facebook options remain fallback-only.
public sealed class GraphPublisherOptions
{
    public const string SectionName = "Content:GraphPublisher";

    public GraphChannelOptions Facebook { get; init; } = new();
    public GraphChannelOptions Zalo { get; init; } = new();
    public bool InstagramPublishingEnabled { get; init; }
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
    IMetaGraphClient? metaGraph = null,
    IInstagramCredentialResolver? instagramCredentialResolver = null,
    IHttpClientFactory? httpClientFactory = null,
    IPublicUrlSafetyValidator? publicUrlSafetyValidator = null) : ISocialPublisher
{
    private const string FacebookEndpoint = "https://graph.facebook.com/v25.0";
    private const string ZaloApiHost = "openapi.zalo.me";
    private const string ZaloApiBasePath = "/v2.0";
    internal const string ZaloHttpClientName = "clawbot-zalo-publisher";
    internal const long ZaloMaxResponseContentBufferSize = 64 * 1024;
    private const int ZaloMaxBodyUtf8Bytes = 256 * 1024;
    private const int ZaloMaxAssetsJsonUtf8Bytes = 32 * 1024;
    private const int ZaloMaxCoverUrlLength = 2048;
    private const string ZaloDefaultAuthor = "Clawbot";
    private const int ZaloTitleMaxLength = 150;
    private const int ZaloDescriptionMaxLength = 300;
    private const int ZaloProcessTokenMaxLength = 4096;
    private const int ZaloArticleIdMaxLength = 256;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = http;
    private readonly GraphPublisherOptions _options = options.Value;
    private readonly ISocialCredentialResolver? _credentialResolver = credentialResolver;
    private readonly ILogger<GraphSocialPublisher> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GraphSocialPublisher>.Instance;
    private readonly IMetaIntegrationService? _metaIntegration = metaIntegration;
    private readonly IMetaGraphClient? _metaGraph = metaGraph;
    private readonly IInstagramCredentialResolver? _instagramCredentialResolver = instagramCredentialResolver;
    private readonly IHttpClientFactory? _httpClientFactory = httpClientFactory;
    private readonly IPublicUrlSafetyValidator _publicUrlSafetyValidator = publicUrlSafetyValidator
        ?? new DnsPublicUrlSafetyValidator(new SystemHostAddressResolver());

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var platform = NormalizePlatform(request.Platform);
        return platform switch
        {
            "facebook" => await PublishFacebookAsync(request, ct).ConfigureAwait(false),
            "instagram" => await PublishInstagramAsync(request, ct).ConfigureAwait(false),
            "zalo" => await PublishZaloAsync(request, ct).ConfigureAwait(false),
            _ => new PublishResult(false, null, $"unsupported_platform:{request.Platform}"),
        };
    }

    // EARS[WHEN publishing to Facebook THE SYSTEM SHALL resolve credentials from the encrypted DB store first,
    // falling back to options only when no DB credential exists, so prod creds never live in appsettings.json]
    private async Task<PublishResult> PublishFacebookAsync(PublishRequest request, CancellationToken ct)
    {
        if (request.MetaAssetId.HasValue && (_metaIntegration is null || _metaGraph is null))
            return new PublishResult(false, null, "facebook_target_unavailable");

        if (_metaIntegration is not null && _metaGraph is not null)
        {
            var page = await _metaIntegration.ResolvePageAsync(request.TenantId, request.MetaAssetId, ct).ConfigureAwait(false);
            if (page is not null)
            {
                if (request.MetaAssetId.HasValue && page.AssetId != request.MetaAssetId.Value)
                    return new PublishResult(false, null, "facebook_target_unavailable");
                return await PublishFacebookWithMetaAsync(request, page, ct).ConfigureAwait(false);
            }
            if (request.MetaAssetId.HasValue)
                return new PublishResult(false, null, "facebook_target_unavailable");

            var snapshot = await _metaIntegration.GetSnapshotAsync(request.TenantId, ct).ConfigureAwait(false);
            if (HasMetaConnection(snapshot))
            {
                var error = string.Equals(snapshot.Status, "reconnect_required", StringComparison.Ordinal)
                    ? "facebook_reconnect_required"
                    : "facebook_meta_unavailable";
                return new PublishResult(false, null, error);
            }
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
            {
                var error = $"facebook_http_{(int)resp.StatusCode}";
                return IsAmbiguousHttpStatus(resp.StatusCode)
                    ? ProviderOutcomeUnknown("facebook", "publish", error)
                    : new PublishResult(false, null, error);
            }

            using var doc = JsonDocument.Parse(body);
            var idEl = doc.RootElement.TryGetProperty("post_id", out var postIdEl)
                ? postIdEl
                : (doc.RootElement.TryGetProperty("id", out var fallbackId) ? fallbackId : default);
            var postId = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(postId))
            {
                return ProviderOutcomeUnknown(
                    "facebook",
                    "publish",
                    "facebook_response_missing_id");
            }
            var postUrl = $"https://www.facebook.com/{postId}";
            LogPublished(_logger, "facebook", request.TenantId, request.ContentItemId, postUrl);
            return new PublishResult(true, postUrl, null, postId);
        }
        catch (BrokenCircuitException)
        {
            return new PublishResult(false, null, "facebook_circuit_open");
        }
        catch (TimeoutRejectedException)
        {
            return ProviderOutcomeUnknown("facebook", "publish", "facebook_timeout");
        }
        catch (HttpRequestException)
        {
            return ProviderOutcomeUnknown("facebook", "publish", "facebook_unavailable");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return ProviderOutcomeUnknown("facebook", "publish", "facebook_timeout");
        }
        catch (JsonException)
        {
            return ProviderOutcomeUnknown("facebook", "publish", "facebook_response_invalid");
        }
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
            return new PublishResult(true, published.Permalink, null, published.PostId);
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            var refreshedPublishAttempted = false;
            try
            {
                var refresh = await _metaIntegration!.RefreshPageAsync(request.TenantId, request.MetaAssetId, ct).ConfigureAwait(false);
                if (refresh.Status == MetaPageRefreshStatus.TargetUnavailable)
                    return new PublishResult(false, null, "facebook_target_unavailable");
                if (refresh.Status != MetaPageRefreshStatus.Resolved || refresh.Credential is null)
                    return new PublishResult(false, null, "facebook_reconnect_required");
                if (request.MetaAssetId.HasValue
                    && refresh.Credential.AssetId != request.MetaAssetId.Value)
                {
                    return new PublishResult(false, null, "facebook_target_unavailable");
                }

                refreshedPublishAttempted = true;
                var published = await _metaGraph!.PublishPageAsync(
                    request.TenantId,
                    refresh.Credential.PageId,
                    refresh.Credential.PageAccessToken,
                    request.Body,
                    imageUrl,
                    ct).ConfigureAwait(false);
                LogPublished(_logger, "facebook", request.TenantId, request.ContentItemId, published.Permalink);
                return new PublishResult(true, published.Permalink, null, published.PostId);
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
                return refreshedPublishAttempted
                    ? FacebookMetaFailure(request, refreshException)
                    : FacebookGraphFailure(request, refreshException);
            }
            catch (HttpRequestException)
            {
                LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, "unavailable");
                return refreshedPublishAttempted
                    ? ProviderOutcomeUnknown("facebook", "publish", "facebook_unavailable")
                    : new PublishResult(false, null, "facebook_unavailable");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return refreshedPublishAttempted
                    ? ProviderOutcomeUnknown("facebook", "publish", "facebook_timeout")
                    : new PublishResult(false, null, "facebook_timeout");
            }
        }
        catch (MetaGraphException ex)
        {
            return FacebookMetaFailure(request, ex);
        }
        catch (HttpRequestException)
        {
            LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, "unavailable");
            return ProviderOutcomeUnknown("facebook", "publish", "facebook_unavailable");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ProviderOutcomeUnknown("facebook", "publish", "facebook_timeout");
        }
    }

    // Instagram first honors a tenant-wide standalone override; absent/disabled overrides keep linked Meta behavior.
    private async Task<PublishResult> PublishInstagramAsync(PublishRequest request, CancellationToken ct)
    {
        if (!_options.InstagramPublishingEnabled)
            return new PublishResult(false, null, "instagram_publishing_disabled");

        var media = ResolveInstagramImage(request.AssetsJson);
        if (media.Error is not null)
            return new PublishResult(false, null, media.Error);

        if (_instagramCredentialResolver is null)
            return new PublishResult(false, null, "instagram_credentials_invalid");

        var standalone = await _instagramCredentialResolver
            .ResolveAsync(request.TenantId, ct)
            .ConfigureAwait(false);
        if (standalone.Status == InstagramCredentialResolutionStatus.Invalid
            || (standalone.Status == InstagramCredentialResolutionStatus.Resolved
                && standalone.Credential is null))
        {
            return new PublishResult(false, null, "instagram_credentials_invalid");
        }
        if (standalone.Status == InstagramCredentialResolutionStatus.Resolved)
        {
            if (request.MetaAssetId.HasValue
                || string.IsNullOrWhiteSpace(request.ProviderTargetId)
                || !string.Equals(
                    standalone.Credential!.InstagramUserId,
                    request.ProviderTargetId,
                    StringComparison.Ordinal))
            {
                return new PublishResult(false, null, "instagram_target_unavailable");
            }

            return await PublishStandaloneInstagramAsync(
                request,
                standalone.Credential,
                media.ImageUrl!,
                ct).ConfigureAwait(false);
        }

        // A standalone snapshot is represented by provider target + no Meta Page. Do not silently
        // fall back to linked Meta when the standalone override is later disabled or removed.
        if (!request.MetaAssetId.HasValue && !string.IsNullOrWhiteSpace(request.ProviderTargetId))
            return new PublishResult(false, null, "instagram_target_unavailable");

        if (_metaIntegration is null || _metaGraph is null)
            return new PublishResult(false, null, "instagram_meta_unavailable");
        if (!request.MetaAssetId.HasValue || string.IsNullOrWhiteSpace(request.ProviderTargetId))
            return new PublishResult(false, null, "instagram_target_required");

        var refreshAssetId = request.MetaAssetId;
        var publishAttempted = false;
        try
        {
            var resolution = await _metaIntegration.ResolveInstagramAsync(
                request.TenantId,
                request.MetaAssetId,
                ct).ConfigureAwait(false);
            if (resolution.Status != MetaInstagramResolutionStatus.Resolved
                || resolution.Credential is null)
            {
                return MapInstagramResolutionFailure(resolution.Status);
            }

            var credential = resolution.Credential;
            refreshAssetId = credential.PageAssetId;
            if (credential.PageAssetId != request.MetaAssetId.Value
                || !string.Equals(
                    credential.InstagramUserId,
                    request.ProviderTargetId,
                    StringComparison.Ordinal))
            {
                return new PublishResult(false, null, "instagram_target_unavailable");
            }

            publishAttempted = true;
            var published = await _metaGraph.PublishInstagramAsync(
                request.TenantId,
                credential.InstagramUserId,
                credential.PageAccessToken,
                request.Body,
                media.ImageUrl!,
                ct).ConfigureAwait(false);
            LogPublished(_logger, "instagram", request.TenantId, request.ContentItemId, published.Permalink ?? published.MediaId);
            return new PublishResult(true, published.Permalink, null, published.MediaId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return publishAttempted
                ? InstagramOutcomeUnknown(request, "instagram_timeout", "timeout")
                : InstagramFailure(request, "instagram_timeout", "timeout");
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            return await RetryInstagramAfterTokenErrorAsync(
                request,
                refreshAssetId,
                media.ImageUrl!,
                ct).ConfigureAwait(false);
        }
        catch (MetaGraphException ex)
        {
            return publishAttempted
                ? InstagramMetaFailure(request, ex)
                : InstagramGraphFailure(request, ex);
        }
        catch (HttpRequestException)
        {
            return publishAttempted
                ? InstagramOutcomeUnknown(request, "instagram_unavailable", "unavailable")
                : InstagramFailure(request, "instagram_unavailable", "unavailable");
        }
        catch (Exception)
        {
            return publishAttempted
                ? InstagramOutcomeUnknown(request, "instagram_error", "unexpected")
                : InstagramFailure(request, "instagram_error", "unexpected");
        }
    }

    private async Task<PublishResult> PublishStandaloneInstagramAsync(
        PublishRequest request,
        InstagramCredential credential,
        string imageUrl,
        CancellationToken ct)
    {
        if (_metaGraph is null)
            return new PublishResult(false, null, "instagram_meta_unavailable");

        try
        {
            var published = await _metaGraph.PublishInstagramAsync(
                request.TenantId,
                credential.InstagramUserId,
                credential.AccessToken,
                request.Body,
                imageUrl,
                ct).ConfigureAwait(false);
            LogPublished(
                _logger,
                "instagram",
                request.TenantId,
                request.ContentItemId,
                published.Permalink ?? published.MediaId);
            return new PublishResult(true, published.Permalink, null, published.MediaId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return InstagramOutcomeUnknown(request, "instagram_timeout", "standalone_timeout");
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            return InstagramFailure(request, "instagram_credentials_invalid", "standalone_credentials_invalid");
        }
        catch (MetaGraphException ex)
        {
            return InstagramMetaFailure(request, ex);
        }
        catch (HttpRequestException)
        {
            return InstagramOutcomeUnknown(request, "instagram_unavailable", "standalone_unavailable");
        }
        catch (Exception)
        {
            return InstagramOutcomeUnknown(request, "instagram_error", "standalone_unexpected");
        }
    }

    private async Task<PublishResult> RetryInstagramAfterTokenErrorAsync(
        PublishRequest request,
        Guid? assetId,
        string imageUrl,
        CancellationToken ct)
    {
        var publishAttempted = false;
        try
        {
            var refresh = await _metaIntegration!.RefreshPageAsync(
                request.TenantId,
                assetId,
                ct).ConfigureAwait(false);
            if (refresh.Status == MetaPageRefreshStatus.TargetUnavailable)
                return new PublishResult(false, null, "instagram_target_unavailable");
            if (refresh.Status != MetaPageRefreshStatus.Resolved || refresh.Credential is null)
                return new PublishResult(false, null, "instagram_reconnect_required");
            if (request.MetaAssetId.HasValue
                && refresh.Credential.AssetId != request.MetaAssetId.Value)
            {
                return new PublishResult(false, null, "instagram_target_unavailable");
            }

            var instagramUserId = await _metaGraph!.ResolveInstagramAccountAsync(
                request.TenantId,
                refresh.Credential.PageId,
                refresh.Credential.PageAccessToken,
                ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(instagramUserId))
                return new PublishResult(false, null, "instagram_not_linked");
            if (!string.Equals(instagramUserId, request.ProviderTargetId, StringComparison.Ordinal))
                return new PublishResult(false, null, "instagram_target_unavailable");

            publishAttempted = true;
            var published = await _metaGraph.PublishInstagramAsync(
                request.TenantId,
                instagramUserId,
                refresh.Credential.PageAccessToken,
                request.Body,
                imageUrl,
                ct).ConfigureAwait(false);
            LogPublished(_logger, "instagram", request.TenantId, request.ContentItemId, published.Permalink ?? published.MediaId);
            return new PublishResult(true, published.Permalink, null, published.MediaId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return publishAttempted
                ? InstagramOutcomeUnknown(request, "instagram_timeout", "retry_timeout")
                : InstagramFailure(request, "instagram_timeout", "retry_timeout");
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            await _metaIntegration!.MarkReconnectRequiredAsync(
                request.TenantId,
                $"meta_token_{ex.Code ?? 0}_{ex.Subcode ?? 0}",
                ct).ConfigureAwait(false);
            return new PublishResult(false, null, "instagram_reconnect_required");
        }
        catch (MetaGraphException ex)
        {
            return publishAttempted
                ? InstagramMetaFailure(request, ex)
                : InstagramGraphFailure(request, ex);
        }
        catch (HttpRequestException)
        {
            return publishAttempted
                ? InstagramOutcomeUnknown(request, "instagram_unavailable", "retry_unavailable")
                : InstagramFailure(request, "instagram_unavailable", "retry_unavailable");
        }
        catch (Exception)
        {
            return publishAttempted
                ? InstagramOutcomeUnknown(request, "instagram_error", "retry_unexpected")
                : InstagramFailure(request, "instagram_error", "retry_unexpected");
        }
    }

    private static PublishResult MapInstagramResolutionFailure(MetaInstagramResolutionStatus status) =>
        status switch
        {
            MetaInstagramResolutionStatus.Disconnected => new PublishResult(false, null, "instagram_meta_unavailable"),
            MetaInstagramResolutionStatus.ReconnectRequired => new PublishResult(false, null, "instagram_reconnect_required"),
            MetaInstagramResolutionStatus.PageUnavailable => new PublishResult(false, null, "instagram_target_unavailable"),
            MetaInstagramResolutionStatus.MissingScopes => new PublishResult(false, null, "instagram_permissions_missing"),
            MetaInstagramResolutionStatus.NotLinked => new PublishResult(false, null, "instagram_not_linked"),
            _ => new PublishResult(false, null, "instagram_meta_unavailable"),
        };

    private PublishResult FacebookMetaFailure(
        PublishRequest request,
        MetaGraphException exception)
    {
        var error = MapMetaPublishError("facebook", exception);
        LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, error);
        return IsAmbiguousMetaFailure(exception)
            ? ProviderOutcomeUnknown("facebook", "publish", error)
            : new PublishResult(false, null, error);
    }

    private PublishResult FacebookGraphFailure(
        PublishRequest request,
        MetaGraphException exception)
    {
        var error = MapMetaPublishError("facebook", exception);
        LogMetaPublishFailed(_logger, request.TenantId, request.ContentItemId, error);
        return new PublishResult(false, null, error);
    }

    private PublishResult InstagramMetaFailure(
        PublishRequest request,
        MetaGraphException exception)
    {
        var error = MapMetaPublishError("instagram", exception);
        return IsAmbiguousMetaFailure(exception)
            ? InstagramOutcomeUnknown(request, error, $"graph_{exception.Code ?? exception.HttpStatus ?? 0}")
            : InstagramFailure(request, error, $"graph_{exception.Code ?? exception.HttpStatus ?? 0}");
    }

    private PublishResult InstagramGraphFailure(PublishRequest request, MetaGraphException exception)
    {
        var code = exception.Code ?? exception.HttpStatus ?? 0;
        return InstagramFailure(request, MapMetaPublishError("instagram", exception), $"graph_{code}");
    }

    private PublishResult InstagramOutcomeUnknown(
        PublishRequest request,
        string error,
        string reason)
    {
        LogInstagramPublishFailed(_logger, request.TenantId, request.ContentItemId, reason);
        return ProviderOutcomeUnknown("instagram", "publish", error);
    }

    private PublishResult InstagramFailure(PublishRequest request, string error, string reason)
    {
        LogInstagramPublishFailed(_logger, request.TenantId, request.ContentItemId, reason);
        return new PublishResult(false, null, error);
    }

    private static string MapMetaPublishError(string platform, MetaGraphException exception)
    {
        if (string.Equals(exception.Message, "meta_response_invalid_json", StringComparison.Ordinal))
            return $"{platform}_response_invalid";
        if (exception.Message.StartsWith("meta_response_missing_", StringComparison.Ordinal))
            return $"{platform}_response_missing_id";
        return $"{platform}_graph_{exception.Code ?? exception.HttpStatus ?? 0}";
    }

    private static bool IsAmbiguousMetaFailure(MetaGraphException exception) =>
        exception.IsTransient
        || string.Equals(exception.Message, "meta_response_invalid_json", StringComparison.Ordinal)
        || exception.Message.StartsWith("meta_response_missing_", StringComparison.Ordinal)
        || exception.HttpStatus is { } status && IsAmbiguousHttpStatusCode(status);

    private static bool IsAmbiguousHttpStatus(System.Net.HttpStatusCode status) =>
        IsAmbiguousHttpStatusCode((int)status);

    private static bool IsAmbiguousHttpStatusCode(int statusCode) =>
        statusCode is 408 or 429 || statusCode >= 500;

    private static PublishResult ProviderOutcomeUnknown(
        string platform,
        string stage,
        string error) =>
        new(false, null, $"{platform}_outcome_unknown:{stage}:{error}");

    private static (string? ImageUrl, string? Error) ResolveInstagramImage(string assetsJson)
    {
        if (string.IsNullOrWhiteSpace(assetsJson))
            return (null, "instagram_media_required");

        try
        {
            using var doc = JsonDocument.Parse(assetsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (null, "instagram_media_invalid");

            var hasImageEntry = false;
            foreach (var asset in doc.RootElement.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object
                    || !asset.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String
                    || !string.Equals(type.GetString(), "image", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasImageEntry = true;
                if (asset.TryGetProperty("url", out var url)
                    && url.ValueKind == JsonValueKind.String
                    && IsPublicJpegUrl(url.GetString(), out var imageUrl))
                {
                    return (imageUrl, null);
                }
            }

            return hasImageEntry
                ? (null, "instagram_media_invalid")
                : (null, "instagram_media_required");
        }
        catch (JsonException)
        {
            return (null, "instagram_media_invalid");
        }
    }

    private static bool IsPublicJpegUrl(string? value, out string? imageUrl)
    {
        imageUrl = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (!uri.AbsolutePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                && !uri.AbsolutePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            || !IsPublicHost(uri))
        {
            return false;
        }

        imageUrl = uri.AbsoluteUri;
        return true;
    }

    private static bool IsPublicHost(Uri uri)
    {
        if (uri.IsLoopback
            || uri.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!System.Net.IPAddress.TryParse(uri.IdnHost, out var address))
        {
            return uri.IdnHost.Contains('.')
                   && Uri.CheckHostName(uri.IdnHost) == UriHostNameType.Dns;
        }
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return !System.Net.IPAddress.IsLoopback(address)
                   && !address.Equals(System.Net.IPAddress.IPv6Any)
                   && !address.Equals(System.Net.IPAddress.IPv6None)
                   && !address.IsIPv6LinkLocal
                   && !address.IsIPv6Multicast
                   && !address.IsIPv6SiteLocal
                   && first is not 0xfc and not 0xfd;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] is not (0 or 10 or 127)
               && bytes[0] < 224
               && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
               && !(bytes[0] == 169 && bytes[1] == 254)
               && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
               && !(bytes[0] == 192 && bytes[1] == 168)
               && !(bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    // EARS[WHEN a tenant credential resolver is configured THE SYSTEM SHALL use only that tenant's Zalo credentials;
    // options fallback is limited to callers constructed without a resolver]
    private async Task<PublishResult> PublishZaloAsync(PublishRequest request, CancellationToken ct)
    {
        if (ExceedsUtf8Limit(request.Body, ZaloMaxBodyUtf8Bytes))
            return new PublishResult(false, null, "zalo_body_too_large");
        if (ExceedsUtf8Limit(request.AssetsJson, ZaloMaxAssetsJsonUtf8Bytes))
            return new PublishResult(false, null, "zalo_assets_too_large");

        var stage = "create";
        var transmissionMayHaveOccurred = false;
        try
        {
            var z = await ResolveZaloChannelAsync(request.TenantId, ct).ConfigureAwait(false);
            if (z is null || !z.Enabled || string.IsNullOrWhiteSpace(z.Endpoint) || string.IsNullOrWhiteSpace(z.OaAccessToken) || string.IsNullOrWhiteSpace(z.OaId))
                return new PublishResult(false, null, "zalo_not_configured");
            if (ContainsControlCharacters(z.OaAccessToken))
                return new PublishResult(false, null, "zalo_credentials_invalid");
            if (!TryBuildZaloArticleEndpoint(z.Endpoint, "create", out var createEndpoint)
                || !TryBuildZaloArticleEndpoint(z.Endpoint, "verify", out var verifyEndpoint))
            {
                return new PublishResult(false, null, "zalo_endpoint_invalid");
            }

            var httpClientFactory = _httpClientFactory;
            if (httpClientFactory is null)
                return new PublishResult(false, null, "zalo_http_client_not_configured");

            var coverUrl = FirstImageUrl(request.AssetsJson);
            if (string.IsNullOrWhiteSpace(coverUrl))
                return new PublishResult(false, null, "zalo_media_required");
            if (!TryNormalizeZaloCoverUrl(coverUrl, out var normalizedCoverUri)
                || !await _publicUrlSafetyValidator
                    .IsSafeAsync(normalizedCoverUri, ct)
                    .ConfigureAwait(false))
            {
                return new PublishResult(false, null, "zalo_media_invalid");
            }

            var normalizedCoverUrl = normalizedCoverUri.AbsoluteUri;
            var zaloHttp = httpClientFactory.CreateClient(ZaloHttpClientName);
            var normalizedBody = request.Body.Trim();
            using var createRequest = CreateZaloRequest(
                createEndpoint,
                z.OaAccessToken,
                new
                {
                    type = "normal",
                    title = Truncate(normalizedBody, ZaloTitleMaxLength),
                    author = ZaloDefaultAuthor,
                    cover = new
                    {
                        cover_type = "photo",
                        photo_url = normalizedCoverUrl,
                        status = "show",
                    },
                    description = Truncate(normalizedBody, ZaloDescriptionMaxLength),
                    body = new[] { new { type = "text", content = request.Body } },
                    status = "show",
                    comment = "show",
                });
            transmissionMayHaveOccurred = true;
            using var createResponse = await zaloHttp.SendAsync(createRequest, ct).ConfigureAwait(false);
            using var createDocument = await ReadZaloResponseAsync(createResponse, ct).ConfigureAwait(false);
            var createError = ResolveZaloError(createResponse, createDocument.RootElement);
            if (createError is not null)
            {
                if (IsAmbiguousHttpStatus(createResponse.StatusCode)
                    || (createResponse.IsSuccessStatusCode
                        && !createError.StartsWith("zalo_error_", StringComparison.Ordinal)))
                {
                    return ZaloOutcomeUnknown(stage, createError);
                }

                return new PublishResult(false, null, createError);
            }

            var processToken = ReadZaloDataString(createDocument.RootElement, "token");
            if (string.IsNullOrWhiteSpace(processToken))
                return ZaloOutcomeUnknown(stage, "zalo_response_missing_token");
            if (processToken.Length > ZaloProcessTokenMaxLength || ContainsControlCharacters(processToken))
                return ZaloOutcomeUnknown(stage, "zalo_response_invalid_token");

            stage = "verify";
            using var verifyRequest = CreateZaloRequest(
                verifyEndpoint,
                z.OaAccessToken,
                new { token = processToken });
            using var verifyResponse = await zaloHttp.SendAsync(verifyRequest, ct).ConfigureAwait(false);
            using var verifyDocument = await ReadZaloResponseAsync(verifyResponse, ct).ConfigureAwait(false);
            var verifyError = ResolveZaloError(verifyResponse, verifyDocument.RootElement);
            if (verifyError is not null)
                return ZaloOutcomeUnknown(stage, verifyError);
            var articleId = ReadZaloDataString(verifyDocument.RootElement, "id");
            if (string.IsNullOrWhiteSpace(articleId))
                return ZaloOutcomeUnknown(stage, "zalo_response_missing_id");
            if (articleId.Length > ZaloArticleIdMaxLength
                || ContainsControlCharacters(articleId)
                || ContainsSecret(articleId, processToken)
                || ContainsSecret(articleId, z.OaAccessToken))
            {
                return ZaloOutcomeUnknown(stage, "zalo_response_invalid_id");
            }

            LogPublished(_logger, "zalo", request.TenantId, request.ContentItemId, articleId);
            return new PublishResult(true, null, null, articleId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TimeoutRejectedException)
        {
            return MapZaloOperationalFailure(stage, transmissionMayHaveOccurred, "zalo_timeout");
        }
        catch (BrokenCircuitException)
        {
            var earlierTransmissionOccurred = string.Equals(stage, "verify", StringComparison.Ordinal);
            return MapZaloOperationalFailure(stage, earlierTransmissionOccurred, "zalo_circuit_open");
        }
        catch (OperationCanceledException)
        {
            return MapZaloOperationalFailure(stage, transmissionMayHaveOccurred, "zalo_timeout");
        }
        catch (ZaloResponseSizeLimitExceededException)
        {
            return MapZaloOperationalFailure(stage, transmissionMayHaveOccurred, "zalo_response_too_large");
        }
        catch (HttpRequestException)
        {
            return MapZaloOperationalFailure(stage, transmissionMayHaveOccurred, "zalo_http_request_failed");
        }
        catch (JsonException)
        {
            return MapZaloOperationalFailure(stage, transmissionMayHaveOccurred, "zalo_response_invalid_json");
        }
    }

    // EARS[WHEN a tenant stores a Zalo article API base THE SYSTEM SHALL accept only the documented HTTPS origin and paths]
    private static bool TryBuildZaloArticleEndpoint(string endpoint, string operation, out Uri articleEndpoint)
    {
        articleEndpoint = null!;
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var configured)
            || configured.Scheme != Uri.UriSchemeHttps
            || !configured.IsDefaultPort
            || !string.Equals(configured.IdnHost, ZaloApiHost, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(configured.UserInfo)
            || !string.IsNullOrEmpty(configured.Query)
            || !string.IsNullOrEmpty(configured.Fragment))
        {
            return false;
        }

        var configuredPath = configured.AbsolutePath.TrimEnd('/');
        if (configuredPath.EndsWith("/oa", StringComparison.OrdinalIgnoreCase))
            configuredPath = configuredPath[..^3];
        if (!string.Equals(configuredPath, ZaloApiBasePath, StringComparison.OrdinalIgnoreCase))
            return false;

        articleEndpoint = new Uri(
            $"https://{ZaloApiHost}{ZaloApiBasePath}/article/{operation}",
            UriKind.Absolute);
        return true;
    }

    private static HttpRequestMessage CreateZaloRequest(Uri url, string accessToken, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        request.Headers.Add("access_token", accessToken);
        return request;
    }

    private static async Task<JsonDocument> ReadZaloResponseAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            return JsonDocument.Parse("{}");

        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private static string? ResolveZaloError(HttpResponseMessage response, JsonElement root)
    {
        if (!response.IsSuccessStatusCode)
            return $"zalo_http_{(int)response.StatusCode}";
        if (root.ValueKind != JsonValueKind.Object)
            return "zalo_response_invalid_shape";

        var hasError = false;
        var error = default(JsonElement);
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "error", StringComparison.Ordinal))
                continue;
            if (hasError)
                return "zalo_response_invalid_error";

            hasError = true;
            error = property.Value;
        }

        if (!hasError
            || error.ValueKind != JsonValueKind.Number
            || !error.TryGetInt32(out var errorCode))
        {
            return "zalo_response_invalid_error";
        }

        return errorCode == 0 ? null : $"zalo_error_{errorCode}";
    }

    private static string? ReadZaloDataString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("data", out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ExceedsUtf8Limit(string? value, int maximumBytes) =>
        value is not null && Encoding.UTF8.GetByteCount(value) > maximumBytes;

    private static bool TryNormalizeZaloCoverUrl(string value, out Uri normalizedUri)
    {
        normalizedUri = null!;
        if (value.Length == 0
            || value.Length > ZaloMaxCoverUrlLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || ContainsControlCharacters(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsoluteUri.Length > ZaloMaxCoverUrlLength)
        {
            return false;
        }

        normalizedUri = uri;
        return true;
    }

    private static bool ContainsControlCharacters(string value) => value.Any(char.IsControl);

    private static bool ContainsSecret(string value, string secret) =>
        !string.IsNullOrEmpty(secret) && value.Contains(secret, StringComparison.Ordinal);

    private static PublishResult MapZaloOperationalFailure(
        string stage,
        bool transmissionMayHaveOccurred,
        string reason) =>
        transmissionMayHaveOccurred
            ? ZaloOutcomeUnknown(stage, reason)
            : new PublishResult(false, null, reason);

    private static PublishResult ZaloOutcomeUnknown(string stage, string reason) =>
        new(false, null, $"zalo_outcome_unknown:{stage}:{reason}");

    // EARS[WHEN a tenant credential resolver is configured THE SYSTEM SHALL NOT fall back to another OA's global credentials]
    private Task<GraphChannelOptions?> ResolveZaloChannelAsync(Guid tenantId, CancellationToken ct) =>
        _credentialResolver is null
            ? Task.FromResult<GraphChannelOptions?>(_options.Zalo)
            : _credentialResolver.ResolveAsync(tenantId, "zalo", ct);

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
                if (asset.ValueKind != JsonValueKind.Object) continue;
                var type = "image";
                if (asset.TryGetProperty("type", out var typeEl))
                {
                    if (typeEl.ValueKind != JsonValueKind.String) continue;
                    type = typeEl.GetString();
                }
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

    private static bool HasMetaConnection(MetaIntegrationSnapshot snapshot) =>
        snapshot.Assets.Count > 0
        || !string.IsNullOrWhiteSpace(snapshot.ClientBusinessId)
        || !string.IsNullOrWhiteSpace(snapshot.SystemUserId)
        || !string.IsNullOrWhiteSpace(snapshot.TokenType);

    private static string Truncate(string value) => Truncate(value, 200);

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];

    [LoggerMessage(EventId = 5203, Level = LogLevel.Information, Message = "GraphSocialPublisher published {platform} content {contentItemId} tenant {tenantId}: {postUrl}")]
    private static partial void LogPublished(ILogger logger, string platform, Guid tenantId, Guid contentItemId, string postUrl);

    [LoggerMessage(EventId = 5204, Level = LogLevel.Warning, Message = "Meta publish failed for content {ContentItemId} tenant {TenantId}: {Reason}")]
    private static partial void LogMetaPublishFailed(ILogger logger, Guid tenantId, Guid contentItemId, string reason);

    [LoggerMessage(EventId = 5205, Level = LogLevel.Warning, Message = "Instagram publish failed for content {ContentItemId} tenant {TenantId}: {Reason}")]
    private static partial void LogInstagramPublishFailed(ILogger logger, Guid tenantId, Guid contentItemId, string reason);
}
