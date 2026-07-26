using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Integrations.Meta;

public static class MetaAuthorizationModes
{
    public const string DevelopmentUser = "development_user";
    public const string BusinessSystemUser = "business_system_user";

    public static string NormalizeOrDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BusinessSystemUser;

        return value.Trim().ToLowerInvariant() switch
        {
            DevelopmentUser => DevelopmentUser,
            BusinessSystemUser => BusinessSystemUser,
            var unsupported => unsupported,
        };
    }

    public static bool IsSupported(string? value)
    {
        var normalized = NormalizeOrDefault(value);
        return normalized is DevelopmentUser or BusinessSystemUser;
    }

    public static string ExpectedTokenType(string? value) =>
        NormalizeOrDefault(value) == DevelopmentUser
            ? MetaConnectionTokenTypes.User
            : MetaConnectionTokenTypes.BusinessIntegrationSystemUser;
}

public static class MetaConnectionTokenTypes
{
    public const string User = "user";
    public const string BusinessIntegrationSystemUser = "business_integration_system_user";

    public static string FromDebugToken(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "USER" => User,
            "BUSINESS_INTEGRATION_SYSTEM_USER" => BusinessIntegrationSystemUser,
            _ => string.Empty,
        };
}

public sealed class MetaGraphOptions
{
    public const string SectionName = "Meta:Graph";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string DialogBaseUrl { get; set; } = "https://www.facebook.com";
    public string ApiVersion { get; set; } = "v25.0";
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string ConfigurationId { get; set; } = string.Empty;
    public string AuthorizationMode { get; set; } = MetaAuthorizationModes.BusinessSystemUser;
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string FrontendReturnUrl { get; set; } = "http://localhost:15876/system";
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId)
        && !string.IsNullOrWhiteSpace(AppSecret)
        && !string.IsNullOrWhiteSpace(ConfigurationId)
        && MetaAuthorizationModes.IsSupported(AuthorizationMode)
        && !string.IsNullOrWhiteSpace(ApiVersion)
        && Uri.TryCreate(BaseUrl, UriKind.Absolute, out _)
        && Uri.TryCreate(DialogBaseUrl, UriKind.Absolute, out _)
        && Uri.TryCreate(RedirectUri, UriKind.Absolute, out _)
        && Uri.TryCreate(FrontendReturnUrl, UriKind.Absolute, out _);

    public bool IsBusinessWebhookConfigured =>
        IsConfigured
        && MetaAuthorizationModes.NormalizeOrDefault(AuthorizationMode) == MetaAuthorizationModes.BusinessSystemUser
        && !string.IsNullOrWhiteSpace(WebhookVerifyToken);
}

public sealed record MetaTokenResponse(string AccessToken, string TokenType, long? ExpiresIn);

public sealed record MetaDebugToken(
    bool IsValid,
    string AppId,
    string Type,
    string UserId,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DataAccessExpiresAt);

public sealed record MetaIdentity(string Id, string ClientBusinessId);

public sealed record MetaPageToken(
    string Id,
    string Name,
    string AccessToken,
    IReadOnlyList<string> Tasks);

public sealed record MetaPublishedPost(string PostId, string Permalink);

public sealed record MetaInstagramPublishedMedia(string MediaId, string? Permalink);

public interface IMetaGraphClient
{
    Task<string> BuildAuthorizationUrlAsync(Guid tenantId, string state, CancellationToken ct = default);
    Task<MetaTokenResponse> ExchangeCodeAsync(Guid tenantId, string code, CancellationToken ct = default);
    Task<MetaDebugToken> DebugTokenAsync(Guid tenantId, string accessToken, CancellationToken ct = default);
    Task<MetaIdentity> GetIdentityAsync(Guid tenantId, string accessToken, CancellationToken ct = default);
    Task<IReadOnlyList<MetaPageToken>> GetPagesAsync(Guid tenantId, string accessToken, CancellationToken ct = default);
    Task<MetaPublishedPost> PublishPageAsync(
        Guid tenantId,
        string pageId,
        string pageAccessToken,
        string message,
        string? imageUrl,
        CancellationToken ct = default);
    Task<string?> ResolveInstagramAccountAsync(
        Guid tenantId,
        string pageId,
        string pageAccessToken,
        CancellationToken ct = default);
    Task<MetaInstagramPublishedMedia> PublishInstagramAsync(
        Guid tenantId,
        string instagramUserId,
        string pageAccessToken,
        string caption,
        string imageUrl,
        CancellationToken ct = default);
    Task<JsonDocument> GetAsync(
        Guid tenantId,
        string relativePath,
        IReadOnlyDictionary<string, string?> query,
        string accessToken,
        CancellationToken ct = default);
    Task<JsonDocument> PostAsync(
        Guid tenantId,
        string relativePath,
        IReadOnlyDictionary<string, string> fields,
        string accessToken,
        CancellationToken ct = default);
    Task SubscribePageFeedAsync(
        Guid tenantId,
        string pageId,
        string pageAccessToken,
        CancellationToken ct = default);
}

public sealed partial class MetaGraphClient(
    HttpClient http,
    IMetaGraphConfigurationResolver configurations,
    ILogger<MetaGraphClient> logger) : IMetaGraphClient
{
    private static readonly TimeSpan InstagramContainerPollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InstagramPermalinkLookupTimeout = TimeSpan.FromSeconds(5);
    private const int InstagramContainerPollAttempts = 30;
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private const int InstagramContainerNotReadyCode = 9007;
    private const int InstagramContainerNotReadySubcode = 2207027;

    private readonly HttpClient _http = http;
    private readonly IMetaGraphConfigurationResolver _configurations = configurations;
    private readonly ILogger<MetaGraphClient> _logger = logger;

    public async Task<string> BuildAuthorizationUrlAsync(
        Guid tenantId,
        string state,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        return BuildUrl(
            $"{options.DialogBaseUrl.TrimEnd('/')}/{ApiVersion(options)}/dialog/oauth",
            new Dictionary<string, string?>
            {
                ["client_id"] = options.AppId,
                ["redirect_uri"] = options.RedirectUri,
                ["config_id"] = options.ConfigurationId,
                ["response_type"] = "code",
                ["override_default_response_type"] = "true",
                ["state"] = state,
            });
    }

    public async Task<MetaTokenResponse> ExchangeCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildGraphUrl(options, "oauth/access_token", query: null))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.AppId,
                ["client_secret"] = options.AppSecret,
                ["redirect_uri"] = options.RedirectUri,
                ["code"] = code,
            }),
        };
        using var doc = await SendAsync(request, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var token = RequiredString(root, "access_token");
        var tokenType = OptionalString(root, "token_type") ?? "bearer";
        long? expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt64(out var seconds)
            ? seconds
            : null;
        return new MetaTokenResponse(token, tokenType, expiresIn);
    }

    public async Task<MetaDebugToken> DebugTokenAsync(
        Guid tenantId,
        string accessToken,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        var url = BuildGraphUrl(options, "debug_token", new Dictionary<string, string?>
        {
            ["input_token"] = accessToken,
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            $"{options.AppId}|{options.AppSecret}");
        using var doc = await SendAsync(request, ct).ConfigureAwait(false);
        var data = doc.RootElement.GetProperty("data");
        var scopes = data.TryGetProperty("scopes", out var scopesElement) && scopesElement.ValueKind == JsonValueKind.Array
            ? scopesElement.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
            : [];

        return new MetaDebugToken(
            data.TryGetProperty("is_valid", out var valid) && valid.ValueKind == JsonValueKind.True,
            OptionalString(data, "app_id") ?? string.Empty,
            OptionalString(data, "type") ?? string.Empty,
            OptionalString(data, "user_id") ?? string.Empty,
            scopes,
            UnixTime(data, "expires_at"),
            UnixTime(data, "data_access_expires_at"));
    }

    public async Task<MetaIdentity> GetIdentityAsync(
        Guid tenantId,
        string accessToken,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        var fields = MetaAuthorizationModes.NormalizeOrDefault(options.AuthorizationMode) == MetaAuthorizationModes.DevelopmentUser
            ? "id"
            : "id,client_business_id";
        using var doc = await GetAsync(
            options,
            "me",
            new Dictionary<string, string?> { ["fields"] = fields },
            accessToken,
            ct).ConfigureAwait(false);
        var root = doc.RootElement;
        return new MetaIdentity(
            RequiredString(root, "id"),
            OptionalString(root, "client_business_id") ?? string.Empty);
    }

    public async Task<IReadOnlyList<MetaPageToken>> GetPagesAsync(
        Guid tenantId,
        string accessToken,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        var pages = new List<MetaPageToken>();
        var seenPageIds = new HashSet<string>(StringComparer.Ordinal);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;

        for (var page = 0; page < 20; page++)
        {
            using var doc = await GetAsync(
                options,
                "me/accounts",
                new Dictionary<string, string?>
                {
                    ["fields"] = "id,name,tasks,access_token",
                    ["limit"] = "100",
                    ["after"] = after,
                },
                accessToken,
                ct).ConfigureAwait(false);

            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var tasks = item.TryGetProperty("tasks", out var taskElement) && taskElement.ValueKind == JsonValueKind.Array
                        ? taskElement.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
                        : [];
                    var id = RequiredString(item, "id");
                    if (seenPageIds.Add(id))
                    {
                        pages.Add(new MetaPageToken(
                            id,
                            RequiredString(item, "name"),
                            RequiredString(item, "access_token"),
                            tasks));
                    }
                }
            }

            after = null;
            if (root.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("next", out var next)
                && next.ValueKind == JsonValueKind.String
                && paging.TryGetProperty("cursors", out var cursors))
            {
                after = OptionalString(cursors, "after");
            }

            if (string.IsNullOrWhiteSpace(after) || !seenCursors.Add(after))
                break;
        }

        return pages;
    }

    public async Task<MetaPublishedPost> PublishPageAsync(
        Guid tenantId,
        string pageId,
        string pageAccessToken,
        string message,
        string? imageUrl,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        var hasImage = !string.IsNullOrWhiteSpace(imageUrl);
        var path = !hasImage ? $"{Uri.EscapeDataString(pageId)}/feed" : $"{Uri.EscapeDataString(pageId)}/photos";
        var fields = !hasImage
            ? new Dictionary<string, string> { ["message"] = message }
            : new Dictionary<string, string> { ["caption"] = message, ["url"] = imageUrl! };

        using var doc = await PostAsync(options, path, fields, pageAccessToken, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var postId = OptionalString(root, "post_id") ?? RequiredString(root, "id");
        return new MetaPublishedPost(postId, $"https://www.facebook.com/{postId}");
    }

    public async Task<string?> ResolveInstagramAccountAsync(
        Guid tenantId,
        string pageId,
        string pageAccessToken,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        using var doc = await GetAsync(
            options,
            Uri.EscapeDataString(pageId),
            new Dictionary<string, string?>
            {
                ["fields"] = "instagram_business_account{id}",
            },
            pageAccessToken,
            ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("instagram_business_account", out var account)
            || account.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return OptionalString(account, "id");
    }

    public async Task<MetaInstagramPublishedMedia> PublishInstagramAsync(
        Guid tenantId,
        string instagramUserId,
        string pageAccessToken,
        string caption,
        string imageUrl,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        var escapedUserId = Uri.EscapeDataString(instagramUserId);
        using var creation = await PostAsync(
            options,
            $"{escapedUserId}/media",
            new Dictionary<string, string>
            {
                ["image_url"] = imageUrl,
                ["caption"] = caption,
            },
            pageAccessToken,
            ct).ConfigureAwait(false);
        var creationId = RequiredString(creation.RootElement, "id");
        await WaitForInstagramContainerAsync(
            options,
            creationId,
            pageAccessToken,
            ct).ConfigureAwait(false);

        var mediaId = await PublishInstagramContainerAsync(
            options,
            escapedUserId,
            creationId,
            pageAccessToken,
            ct).ConfigureAwait(false);
        using var permalinkCts = new CancellationTokenSource(InstagramPermalinkLookupTimeout);
        var permalink = await ResolveInstagramPermalinkAsync(
            options,
            tenantId,
            mediaId,
            pageAccessToken,
            permalinkCts.Token).ConfigureAwait(false);
        return new MetaInstagramPublishedMedia(mediaId, permalink);
    }

    private async Task<string> PublishInstagramContainerAsync(
        MetaGraphOptions options,
        string escapedUserId,
        string creationId,
        string pageAccessToken,
        CancellationToken ct)
    {
        try
        {
            return await PublishOnceAsync().ConfigureAwait(false);
        }
        catch (MetaGraphException ex) when (IsInstagramContainerNotReady(ex))
        {
            await WaitForInstagramContainerAsync(
                options,
                creationId,
                pageAccessToken,
                ct).ConfigureAwait(false);
            return await PublishOnceAsync().ConfigureAwait(false);
        }

        async Task<string> PublishOnceAsync()
        {
            using var published = await PostAsync(
                options,
                $"{escapedUserId}/media_publish",
                new Dictionary<string, string>
                {
                    ["creation_id"] = creationId,
                },
                pageAccessToken,
                ct).ConfigureAwait(false);
            return RequiredString(published.RootElement, "id");
        }
    }

    private async Task WaitForInstagramContainerAsync(
        MetaGraphOptions options,
        string creationId,
        string pageAccessToken,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= InstagramContainerPollAttempts; attempt++)
        {
            using var statusDocument = await GetAsync(
                options,
                Uri.EscapeDataString(creationId),
                new Dictionary<string, string?> { ["fields"] = "status_code,status" },
                pageAccessToken,
                ct).ConfigureAwait(false);
            var status = OptionalString(statusDocument.RootElement, "status_code")
                ?? OptionalString(statusDocument.RootElement, "status")
                ?? string.Empty;
            if (string.Equals(status, "FINISHED", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                throw new MetaGraphException($"instagram_container_{status.ToLowerInvariant()}");
            }
            if (attempt < InstagramContainerPollAttempts)
                await Task.Delay(InstagramContainerPollDelay, ct).ConfigureAwait(false);
        }

        throw new MetaGraphException(
            "instagram_container_not_ready",
            code: InstagramContainerNotReadyCode,
            subcode: InstagramContainerNotReadySubcode,
            isTransient: true);
    }

    private async Task<string?> ResolveInstagramPermalinkAsync(
        MetaGraphOptions options,
        Guid tenantId,
        string mediaId,
        string pageAccessToken,
        CancellationToken ct)
    {
        try
        {
            using var doc = await GetAsync(
                options,
                Uri.EscapeDataString(mediaId),
                new Dictionary<string, string?> { ["fields"] = "permalink" },
                pageAccessToken,
                ct).ConfigureAwait(false);
            return OptionalString(doc.RootElement, "permalink");
        }
        catch (MetaGraphException ex)
        {
            LogInstagramPermalinkLookupFailed(_logger, tenantId, mediaId, ex.Code ?? ex.HttpStatus ?? 0);
            return null;
        }
        catch (HttpRequestException)
        {
            LogInstagramPermalinkLookupFailed(_logger, tenantId, mediaId, 0);
            return null;
        }
        catch (OperationCanceledException)
        {
            LogInstagramPermalinkLookupFailed(_logger, tenantId, mediaId, 0);
            return null;
        }
    }

    private static bool IsInstagramContainerNotReady(MetaGraphException exception) =>
        exception.Code == InstagramContainerNotReadyCode
        && exception.Subcode == InstagramContainerNotReadySubcode;

    public async Task<JsonDocument> GetAsync(
        Guid tenantId,
        string relativePath,
        IReadOnlyDictionary<string, string?> query,
        string accessToken,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        return await GetAsync(options, relativePath, query, accessToken, ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetAsync(
        MetaGraphOptions options,
        string relativePath,
        IReadOnlyDictionary<string, string?> query,
        string accessToken,
        CancellationToken ct)
    {
        var signed = new Dictionary<string, string?>(query, StringComparer.Ordinal);
        AddNullableProof(options, signed, accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildGraphUrl(options, relativePath, signed));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<JsonDocument> PostAsync(
        Guid tenantId,
        string relativePath,
        IReadOnlyDictionary<string, string> fields,
        string accessToken,
        CancellationToken ct = default)
    {
        var options = await GetConfiguredAsync(tenantId, ct).ConfigureAwait(false);
        return await PostAsync(options, relativePath, fields, accessToken, ct).ConfigureAwait(false);
    }

    public async Task SubscribePageFeedAsync(
        Guid tenantId,
        string pageId,
        string pageAccessToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pageId)
            || !pageId.Trim().All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            throw new ArgumentException("pageId invalid", nameof(pageId));
        }
        if (string.IsNullOrWhiteSpace(pageAccessToken))
            throw new ArgumentException("pageAccessToken required", nameof(pageAccessToken));

        using var document = await PostAsync(
            tenantId,
            $"{pageId.Trim()}/subscribed_apps",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["subscribed_fields"] = "feed",
            },
            pageAccessToken,
            ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument> PostAsync(
        MetaGraphOptions options,
        string relativePath,
        IReadOnlyDictionary<string, string> fields,
        string accessToken,
        CancellationToken ct)
    {
        var signed = new Dictionary<string, string>(fields, StringComparer.Ordinal)
        {
            ["access_token"] = accessToken,
        };
        AddProof(options, signed, accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl(options, relativePath, null))
        {
            Content = new FormUrlEncodedContent(signed),
        };
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task<string?> ReadResponseBodyAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
            if (total > MaxResponseBytes)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var body = response.Content is null
            ? string.Empty
            : await ReadResponseBodyAsync(response.Content, ct).ConfigureAwait(false);
        if (body is null)
            throw new MetaGraphException("meta_response_too_large", (int)response.StatusCode);
        LogUsageHeaders(response);

        JsonDocument? parsed = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try { parsed = JsonDocument.Parse(body); }
            catch (JsonException) when (response.IsSuccessStatusCode) { throw new MetaGraphException("meta_response_invalid_json", (int)response.StatusCode); }
        }

        if (!response.IsSuccessStatusCode || parsed?.RootElement.TryGetProperty("error", out _) == true)
        {
            var exception = MetaGraphException.FromResponse(response.StatusCode, parsed?.RootElement, body);
            parsed?.Dispose();
            throw exception;
        }

        return parsed ?? JsonDocument.Parse("{}");
    }

    private static void AddNullableProof(
        MetaGraphOptions options,
        Dictionary<string, string?> fields,
        string accessToken)
    {
        if (string.IsNullOrWhiteSpace(options.AppSecret))
            return;

        fields["appsecret_proof"] = GenerateProof(options, accessToken);
    }

    private static void AddProof(
        MetaGraphOptions options,
        Dictionary<string, string> fields,
        string accessToken)
    {
        if (string.IsNullOrWhiteSpace(options.AppSecret))
            return;

        fields["appsecret_proof"] = GenerateProof(options, accessToken);
    }

    private static string GenerateProof(MetaGraphOptions options, string accessToken)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.AppSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(accessToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildGraphUrl(
        MetaGraphOptions options,
        string relativePath,
        IReadOnlyDictionary<string, string?>? query) =>
        BuildUrl($"{options.BaseUrl.TrimEnd('/')}/{ApiVersion(options)}/{relativePath.TrimStart('/')}", query);

    private static string BuildUrl(string baseUrl, IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
            return baseUrl;

        var parts = query
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}");
        return $"{baseUrl}?{string.Join("&", parts)}";
    }

    private static string ApiVersion(MetaGraphOptions options) => options.ApiVersion.Trim().Trim('/');

    private async Task<MetaGraphOptions> GetConfiguredAsync(Guid tenantId, CancellationToken ct)
    {
        var options = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (!options.IsConfigured)
            throw new InvalidOperationException("Meta:Graph AppId, AppSecret, ConfigurationId, ApiVersion and absolute URLs are required.");
        return options;
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = OptionalString(element, property);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new MetaGraphException($"meta_response_missing_{property}");
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? UnixTime(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || !value.TryGetInt64(out var seconds) || seconds <= 0)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private void LogUsageHeaders(HttpResponseMessage response)
    {
        foreach (var header in new[] { "X-App-Usage", "X-Ad-Account-Usage", "X-Business-Use-Case-Usage" })
        {
            if (response.Headers.TryGetValues(header, out var values))
                LogUsage(_logger, header, string.Join(",", values));
        }
    }

    [LoggerMessage(EventId = 5250, Level = LogLevel.Debug, Message = "Meta Graph usage {Header}: {Value}")]
    private static partial void LogUsage(ILogger logger, string header, string value);

    [LoggerMessage(EventId = 5251, Level = LogLevel.Warning, Message = "Meta Instagram permalink lookup failed for tenant {TenantId}, media {MediaId}, code {ErrorCode}")]
    private static partial void LogInstagramPermalinkLookupFailed(
        ILogger logger,
        Guid tenantId,
        string mediaId,
        int errorCode);
}

public sealed class MetaGraphException : Exception
{
    public MetaGraphException(
        string message,
        int? httpStatus = null,
        int? code = null,
        int? subcode = null,
        string? errorType = null,
        bool isTransient = false) : base(message)
    {
        HttpStatus = httpStatus;
        Code = code;
        Subcode = subcode;
        ErrorType = errorType;
        IsTransient = isTransient;
    }

    public int? HttpStatus { get; }
    public int? Code { get; }
    public int? Subcode { get; }
    public string? ErrorType { get; }
    public bool IsTransient { get; }
    public bool IsTokenError => Code == 190 || Subcode is 458 or 459 or 460 or 463 or 467;

    internal static MetaGraphException FromResponse(HttpStatusCode status, JsonElement? root, string rawBody)
    {
        if (root is { } value && value.TryGetProperty("error", out var error))
        {
            return new MetaGraphException(
                OptionalString(error, "message") ?? $"meta_http_{(int)status}",
                (int)status,
                OptionalInt(error, "code"),
                OptionalInt(error, "error_subcode"),
                OptionalString(error, "type"),
                error.TryGetProperty("is_transient", out var transient) && transient.ValueKind == JsonValueKind.True);
        }

        var message = string.IsNullOrWhiteSpace(rawBody) ? $"meta_http_{(int)status}" : rawBody[..Math.Min(rawBody.Length, 500)];
        return new MetaGraphException(message, (int)status);
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;
}
