using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;

namespace Clawbot.Infrastructure.Channels.Pancake;

public sealed class PancakeChannelAdapter(
    HttpClient http,
    IPancakeConfigResolver resolver,
    ITenantAccessor tenants,
    IPancakePageTokenResolver? pageTokenResolver = null) : IChannelAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Verified Pancake outbound rate limit (SPEC-16 §5.1): 5 req/s per page -> 429 on overflow.
    // Keyed by page_id when known (per-page bucket); falls back to a tenant bucket for legacy single-page sends.
    private static readonly PartitionedRateLimiter<string> OutboundLimiter =
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 5,
                TokensPerPeriod = 5,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 5,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }));

    private readonly HttpClient _http = http;
    private readonly IPancakeConfigResolver _resolver = resolver;
    private readonly ITenantAccessor _tenants = tenants;
    private readonly IPancakePageTokenResolver? _pageTokenResolver = pageTokenResolver;

    public string Name => "pancake";

    public async Task<bool> VerifyWebhookSignatureAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var cfg = await CurrentConfigAsync(ct).ConfigureAwait(false);
        if (cfg is null || string.IsNullOrEmpty(cfg.WebhookSecret)) return false;

        var lookup = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        if (!lookup.TryGetValue(cfg.SignatureHeader, out var sig) || string.IsNullOrEmpty(sig))
            return false;

        if (!string.Equals(cfg.SignatureAlgo, "hmac-sha256", StringComparison.Ordinal))
            return false;

        return cfg.SignatureEncoding switch
        {
            "base64" => HmacSignatureVerifier.VerifyBase64Sha256(rawBody, sig, cfg.WebhookSecret),
            _ => HmacSignatureVerifier.VerifyHexSha256(rawBody, sig, cfg.WebhookSecret),
        };
    }

    public Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return Task.FromResult<IReadOnlyList<ChannelMessage>>(Array.Empty<ChannelMessage>());

        PancakeWebhookPayload? payload;
        try { payload = JsonSerializer.Deserialize<PancakeWebhookPayload>(rawBody, JsonOpts); }
        catch (JsonException) { payload = null; }
        if (payload?.Events is null || payload.Events.Count == 0)
            return Task.FromResult<IReadOnlyList<ChannelMessage>>(Array.Empty<ChannelMessage>());

        var list = new List<ChannelMessage>(payload.Events.Count);
        foreach (var evt in payload.Events)
        {
            if (string.IsNullOrEmpty(evt.ThreadId) || string.IsNullOrEmpty(evt.Text)) continue;

            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(evt.MessageId)) meta["external_message_id"] = evt.MessageId;
            if (!string.IsNullOrEmpty(evt.SenderName))
            {
                meta["display_name"] = evt.SenderName;
                meta["sender_name"] = evt.SenderName;
            }
            if (!string.IsNullOrEmpty(evt.PageId)) meta["page_id"] = evt.PageId;
            if (!string.IsNullOrEmpty(evt.Type)) meta["event_type"] = evt.Type;
            if (!string.IsNullOrEmpty(evt.SenderId)) meta["sender_id"] = evt.SenderId;

            var messageType = NormalizeMessageType(evt.Type);
            var externalThreadId = string.IsNullOrEmpty(evt.PageId) ? evt.ThreadId : $"{evt.PageId}:{evt.ThreadId}";
            list.Add(new ChannelMessage(
                Channel: evt.Platform ?? "pancake",
                ExternalThreadId: externalThreadId,
                ExternalUserId: evt.SenderId ?? string.Empty,
                Text: evt.Text,
                SentAt: evt.SentAt ?? DateTimeOffset.UtcNow,
                Metadata: meta,
                MessageType: messageType,
                ParentPostId: messageType == "comment" ? evt.PostId : null));
        }

        return Task.FromResult<IReadOnlyList<ChannelMessage>>(list);
    }

    public Task SendAsync(string externalThreadId, string text, CancellationToken ct = default) =>
        SendAsync(externalThreadId, text, accessToken: null, ct);

    public async Task SendAsync(string externalThreadId, string text, string? accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(externalThreadId))
            throw new ArgumentException("thread id required", nameof(externalThreadId));

        var tenantId = _tenants.Current?.TenantId ?? Guid.Empty;
        var (threadPart, pagePart) = SplitThread(externalThreadId);

        // EARS[WHEN sending to a Pancake thread THE SYSTEM SHALL rate-limit per page (5/s) so a multi-page tenant
        // does not share one bucket; WHEN no page is identifiable THE SYSTEM SHALL fall back to a tenant bucket]
        var rateKey = string.IsNullOrEmpty(pagePart) ? $"tenant:{tenantId}" : $"page:{pagePart}";
        using var lease = await OutboundLimiter.AcquireAsync(rateKey, 1, ct).ConfigureAwait(false);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("Pancake outbound rate limit exceeded for page/tenant.");

        var cfg = await CurrentConfigAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Pancake config not resolved for current tenant.");
        if (string.IsNullOrEmpty(pagePart) && !string.IsNullOrEmpty(cfg.PageId))
            pagePart = cfg.PageId;

        // EARS[WHEN a page is identifiable and no explicit token is passed THE SYSTEM SHALL resolve the stored
        // page access token for that page (page ops require a page token, NOT the user token); WHEN none is stored
        // THE SYSTEM SHALL fall back to the legacy configured token so a single-page tenant still works]
        string outboundToken;
        if (string.IsNullOrWhiteSpace(accessToken)
            && !string.IsNullOrEmpty(pagePart)
            && _pageTokenResolver is not null)
        {
            var pageToken = await _pageTokenResolver.ResolveAsync(tenantId, pagePart, ct).ConfigureAwait(false);
            outboundToken = pageToken?.PageAccessToken ?? cfg.AccessToken;
        }
        else
        {
            outboundToken = string.IsNullOrWhiteSpace(accessToken) ? cfg.AccessToken : accessToken;
        }
        if (string.IsNullOrEmpty(outboundToken))
            throw new InvalidOperationException("Pancake access_token not configured.");

        var path = cfg.SendPathTemplate
            .Replace("{page_id}", pagePart, StringComparison.Ordinal)
            .Replace("{thread_id}", threadPart, StringComparison.Ordinal);
        var url = $"{cfg.BaseUrl.TrimEnd('/')}{path}";
        if (string.Equals(cfg.AuthMode, "query", StringComparison.Ordinal))
            url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") +
                   "page_access_token=" + Uri.EscapeDataString(outboundToken);

        var payload = new SendBody("reply_inbox", text);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        if (string.Equals(cfg.AuthMode, "bearer", StringComparison.Ordinal))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outboundToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<PancakeRuntimeConfig?> CurrentConfigAsync(CancellationToken ct)
    {
        var tenant = _tenants.Current;
        var tenantId = tenant?.TenantId ?? Guid.Empty;
        return await _resolver.ResolveAsync(tenantId, ct).ConfigureAwait(false);
    }

    private static (string ThreadId, string PageId) SplitThread(string composite)
    {
        var idx = composite.IndexOf(':', StringComparison.Ordinal);
        return idx > 0
            ? (composite[(idx + 1)..], composite[..idx])
            : (composite, string.Empty);
    }

    private static string NormalizeMessageType(string? type)
    {
        if (string.Equals(type, "COMMENT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "comment", StringComparison.OrdinalIgnoreCase))
            return "comment";

        if (string.Equals(type, "DM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "DIRECT_MESSAGE", StringComparison.OrdinalIgnoreCase))
            return "dm";

        return "text";
    }

    private sealed record SendBody(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("message")] string Message);

    private sealed record PancakeWebhookPayload(
        [property: JsonPropertyName("events")] IReadOnlyList<PancakeEvent>? Events);

    private sealed record PancakeEvent(
        [property: JsonPropertyName("platform")] string? Platform,
        [property: JsonPropertyName("page_id")] string? PageId,
        [property: JsonPropertyName("thread_id")] string? ThreadId,
        [property: JsonPropertyName("message_id")] string? MessageId,
        [property: JsonPropertyName("sender_id")] string? SenderId,
        [property: JsonPropertyName("sender_name")] string? SenderName,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("post_id")] string? PostId,
        [property: JsonPropertyName("sent_at")] DateTimeOffset? SentAt);
}
