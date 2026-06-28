using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Content.Publishing;

// SPEC-16 P2-8: publisher that calls FB Graph POST /{page_id}/feed + Zalo OA article API with a page token,
// replacing the generic webhook publisher_not_configured stub. Channel credentials (FB app, Zalo OA) are read
// from options now (PublisherOptions-style); Module M-1 moves them to encrypted DB storage with per-page tokens.
//
// ponytail: external dep guard — this is scaffolded + unit-tested against a fake HTTP handler. Real publish
// requires FB app id/secret + pages_manage_posts permission (app review, long lead) and a Zalo OA token, which
// the user must provision. The shape is correct against the FB Graph /feed contract and Zalo OA article API.
public sealed class GraphPublisherOptions
{
    public const string SectionName = "Content:GraphPublisher";

    public GraphChannelOptions Facebook { get; init; } = new()
    {
        Endpoint = "https://graph.facebook.com/v21.0",
    };
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
    ILogger<GraphSocialPublisher>? logger = null) : ISocialPublisher
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = http;
    private readonly GraphPublisherOptions _options = options.Value;
    private readonly ISocialCredentialResolver? _credentialResolver = credentialResolver;
    private readonly ILogger<GraphSocialPublisher> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GraphSocialPublisher>.Instance;

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
        var fb = await ResolveChannelAsync(request.TenantId, "facebook", ct).ConfigureAwait(false);
        if (fb is null || !fb.Enabled || string.IsNullOrWhiteSpace(fb.Endpoint) || string.IsNullOrWhiteSpace(fb.PageAccessToken) || string.IsNullOrWhiteSpace(fb.PageId))
            return new PublishResult(false, null, "facebook_not_configured");

        var url = $"{fb.Endpoint.TrimEnd('/')}/{Uri.EscapeDataString(fb.PageId)}/feed";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["message"] = request.Body,
            ["access_token"] = fb.PageAccessToken,
        });

        try
        {
            using var resp = await _http.PostAsync(url, form, ct).ConfigureAwait(false);
            var body = resp.Content is null ? string.Empty : await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new PublishResult(false, null, $"facebook_http_{(int)resp.StatusCode}:{Truncate(body)}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                return new PublishResult(false, null, "facebook_response_missing_id");
            var postId = idEl.GetString()!;
            var postUrl = $"https://www.facebook.com/{fb.PageId}/posts/{ExtractPostId(postId)}";
            LogPublished(_logger, "facebook", request.TenantId, request.ContentItemId, postUrl);
            return new PublishResult(true, postUrl, null);
        }
        catch (HttpRequestException ex) { return new PublishResult(false, null, ex.Message); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new PublishResult(false, null, "facebook_timeout"); }
        catch (JsonException ex) { return new PublishResult(false, null, $"facebook_parse:{ex.Message}"); }
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

    private static string NormalizePlatform(string platform) =>
        (platform ?? string.Empty).Trim().ToLowerInvariant();

    // FB /feed returns "{pageId}_{postId}"; the permalink uses just the postId.
    private static string ExtractPostId(string composite)
    {
        var idx = composite.IndexOf('_');
        return idx > 0 ? composite[(idx + 1)..] : composite;
    }

    private static string Truncate(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length > 200 ? s[..200] : s);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Information, Message = "GraphSocialPublisher published {platform} content {contentItemId} tenant {tenantId}: {postUrl}")]
    private static partial void LogPublished(ILogger logger, string platform, Guid tenantId, Guid contentItemId, string postUrl);
}
