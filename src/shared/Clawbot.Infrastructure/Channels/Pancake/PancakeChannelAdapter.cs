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
    IPancakePageTokenResolver? pageTokenResolver = null) : IChannelAdapter, ICommentChannelAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

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
        Guid tenantId,
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return false;
        ArgumentNullException.ThrowIfNull(headers);
        var cfg = await _resolver.ResolveTenantOnlyAsync(tenantId, ct).ConfigureAwait(false);
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
            // Format webhook THẬT của Pancake (developer.pancake.biz/webhook):
            // {page_id, event_type: "messaging", data: {conversation, message, post}} — khác hẳn
            // format events[] phía trên (giữ lại cho tích hợp cũ/test). Không parse được nhánh này
            // thì CommentAutoReplyJob không bao giờ chạy qua webhook.
            return Task.FromResult<IReadOnlyList<ChannelMessage>>(ParseMessagingWebhook(rawBody));

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
            if (!string.IsNullOrEmpty(evt.AvatarUrl)) meta["page_avatar_url"] = evt.AvatarUrl;

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

    public Task<string?> SendAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string text,
        CancellationToken ct = default) =>
        SendAsync(tenantId, platform, externalThreadId, text, accessToken: null, ct);

    public Task<string?> SendAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string text,
        string? accessToken,
        CancellationToken ct = default) =>
        SendPayloadAsync(tenantId, platform, externalThreadId, accessToken,
            senderId => new SendBody("reply_inbox", text, senderId), ct);

    // Comment auto-reply: rep công khai dưới comment (spec: action reply_comment + message_id).
    public Task<string?> SendCommentReplyAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string commentMessageId,
        string text,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(commentMessageId))
            throw new ArgumentException("comment message id required", nameof(commentMessageId));
        return SendPayloadAsync(tenantId, platform, externalThreadId, accessToken: null,
            senderId => new SendBody("reply_comment", text, senderId, MessageId: commentMessageId), ct);
    }

    // Private reply từ comment (spec: action private_replies + post_id + message_id + from_id; FB/IG only).
    public Task<string?> SendPrivateReplyAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string postId,
        string commentMessageId,
        string fromId,
        string text,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(postId)) throw new ArgumentException("post id required", nameof(postId));
        if (string.IsNullOrEmpty(commentMessageId)) throw new ArgumentException("comment message id required", nameof(commentMessageId));
        if (string.IsNullOrEmpty(fromId)) throw new ArgumentException("from id required", nameof(fromId));
        return SendPayloadAsync(tenantId, platform, externalThreadId, accessToken: null,
            senderId => new SendBody("private_replies", text, senderId, MessageId: commentMessageId, PostId: postId, FromId: fromId), ct);
    }

    private async Task<string?> SendPayloadAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string? accessToken,
        Func<string?, SendBody> payloadFactory,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenant id required", nameof(tenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        if (string.IsNullOrEmpty(externalThreadId))
            throw new ArgumentException("thread id required", nameof(externalThreadId));

        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        if (normalizedPlatform.Length > 32)
            throw new ArgumentOutOfRangeException(
                nameof(platform),
                "Platform must not exceed 32 characters.");

        var (threadPart, pagePart) = SplitThread(externalThreadId);
        var cfg = await _resolver.ResolveAsync(tenantId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Pancake config not resolved for specified tenant.");
        if (string.IsNullOrEmpty(pagePart) && !string.IsNullOrEmpty(cfg.PageId))
            pagePart = cfg.PageId;
        if (string.IsNullOrEmpty(pagePart))
            throw new InvalidOperationException("Pancake page id not configured for specified tenant.");

        // EARS[WHEN sending to a Pancake thread THE SYSTEM SHALL rate-limit per tenant + page (5/s) so tenants
        // never share a bucket; WHEN no page is identifiable THE SYSTEM SHALL fall back to the tenant bucket]
        var rateKey = string.IsNullOrEmpty(pagePart)
            ? $"tenant:{tenantId}:platform:{normalizedPlatform}"
            : $"tenant:{tenantId}:platform:{normalizedPlatform}:page:{pagePart}";
        using var lease = await OutboundLimiter.AcquireAsync(rateKey, 1, ct).ConfigureAwait(false);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("Pancake outbound rate limit exceeded for page/tenant.");

        // Tenant-scoped sends must use a page token owned by that tenant. Never fall back to a
        // process-wide access token for a page the tenant has not connected.
        string outboundToken;
        string? senderId = null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var pageToken = _pageTokenResolver is null
                ? null
                : await _pageTokenResolver.ResolveAsync(
                    tenantId,
                    normalizedPlatform,
                    pagePart,
                    ct).ConfigureAwait(false);
            outboundToken = pageToken?.PageAccessToken
                ?? throw new InvalidOperationException("Pancake page token not configured for specified tenant/page.");
            senderId = pageToken.SenderId;
        }
        else
        {
            outboundToken = accessToken;
        }
        if (string.IsNullOrEmpty(outboundToken))
            throw new InvalidOperationException("Pancake access_token not configured.");

        // Send API only supports v1 (polling/webhook use v2).
        var sendBaseUrl = PancakeEndpointPolicy.NormalizeBaseUrl(cfg.BaseUrl)
            .Replace("/v2/", "/v1/")
            .Replace("/v2", "/v1");

        var path = cfg.SendPathTemplate
            .Replace("{page_id}", pagePart, StringComparison.Ordinal)
            .Replace("{thread_id}", threadPart, StringComparison.Ordinal);
        var url = $"{sendBaseUrl.TrimEnd('/')}{path}";
        if (string.Equals(cfg.AuthMode, "query", StringComparison.Ordinal))
            url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") +
                   "page_access_token=" + Uri.EscapeDataString(outboundToken);

        var payload = payloadFactory(senderId);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload, options: JsonOpts),
        };
        if (string.Equals(cfg.AuthMode, "bearer", StringComparison.Ordinal))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outboundToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new ChannelSendRejectedException("pancake_http_rejected", (int)resp.StatusCode);

        // Send response: {success, id, message} - id la message id phia Pancake, cung id-space voi
        // GET messages cua poller -> caller luu vao ExternalMessageId de dedup strict tin echo.
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        SendResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SendResponse>(body, JsonOpts);
        }
        catch (JsonException ex)
        {
            // HTTP thành công nhưng body không xác nhận được: provider có thể đã nhận tin.
            throw new ChannelDeliveryAmbiguousException("pancake_response_unconfirmed", ex);
        }

        if (parsed?.Success == false)
            throw new ChannelSendRejectedException("pancake_send_rejected");
        if (parsed?.Success != true)
            throw new ChannelDeliveryAmbiguousException("pancake_response_unconfirmed");

        return string.IsNullOrEmpty(parsed.Id) ? null : parsed.Id;
    }

    private static ChannelMessage[] ParseMessagingWebhook(string rawBody)
    {
        PancakeMessagingWebhook? hook;
        try { hook = JsonSerializer.Deserialize<PancakeMessagingWebhook>(rawBody, JsonOpts); }
        catch (JsonException) { return Array.Empty<ChannelMessage>(); }

        if (hook is null
            || !string.Equals(hook.EventType, "messaging", StringComparison.OrdinalIgnoreCase)
            || hook.Data?.Message is not { } m)
            return Array.Empty<ChannelMessage>();

        var conv = hook.Data.Conversation;
        var pageId = hook.PageId ?? m.PageId ?? string.Empty;
        var convId = conv?.Id ?? m.ConversationId ?? string.Empty;
        var text = !string.IsNullOrWhiteSpace(m.Message) ? m.Message : m.OriginalMessage;
        if (string.IsNullOrEmpty(convId) || string.IsNullOrWhiteSpace(text))
            return Array.Empty<ChannelMessage>();

        var senderId = m.From?.Id ?? string.Empty;
        var meta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event_type"] = "messaging",
        };
        if (!string.IsNullOrEmpty(m.Id)) meta["external_message_id"] = m.Id;
        if (!string.IsNullOrEmpty(pageId)) meta["page_id"] = pageId;
        if (!string.IsNullOrEmpty(senderId)) meta["sender_id"] = senderId;
        if (!string.IsNullOrEmpty(m.From?.Name)) meta["sender_name"] = m.From.Name;
        // Page tự rep (echo) — cùng cờ is_owner như poller để ingestor set direction=out + khỏi auto-reply.
        if (!string.IsNullOrEmpty(senderId) && string.Equals(senderId, pageId, StringComparison.Ordinal))
            meta["is_owner"] = "true";

        var isComment = string.Equals(m.Type, "COMMENT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(conv?.Type, "COMMENT", StringComparison.OrdinalIgnoreCase);
        var postId = hook.Data.Post?.Id ?? conv?.PostId;

        return
        [
            new ChannelMessage(
                Channel: PlatformFromPageId(pageId),
                ExternalThreadId: string.IsNullOrEmpty(pageId) ? convId : $"{pageId}:{convId}",
                ExternalUserId: senderId,
                Text: text!,
                SentAt: m.InsertedAt.HasValue ? new DateTimeOffset(m.InsertedAt.Value, TimeSpan.Zero) : DateTimeOffset.UtcNow,
                Metadata: meta,
                MessageType: isComment ? "comment" : "text",
                ParentPostId: isComment ? postId : null),
        ];
    }

    // Pancake prefix page id theo nền tảng (pzl_ zalo cá nhân, tt_ tiktok, ig_ instagram); FB page id thuần số.
    private static string PlatformFromPageId(string pageId)
    {
        if (pageId.StartsWith("pzl_", StringComparison.OrdinalIgnoreCase)) return "zalo";
        if (pageId.StartsWith("tt_", StringComparison.OrdinalIgnoreCase)) return "tiktok";
        if (pageId.StartsWith("ig_", StringComparison.OrdinalIgnoreCase)) return "instagram";
        return "facebook";
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
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("sender_id")] string? SenderId = null,
        [property: JsonPropertyName("message_id")] string? MessageId = null,
        [property: JsonPropertyName("post_id")] string? PostId = null,
        [property: JsonPropertyName("from_id")] string? FromId = null);
    private sealed record SendResponse(
        [property: JsonPropertyName("success")] bool? Success,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("message")] string? Message);
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
        [property: JsonPropertyName("sent_at")] DateTimeOffset? SentAt,
        [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

    // Format webhook chính thức của Pancake (event_type=messaging).
    private sealed record PancakeMessagingWebhook(
        [property: JsonPropertyName("page_id")] string? PageId,
        [property: JsonPropertyName("event_type")] string? EventType,
        [property: JsonPropertyName("data")] PancakeMessagingData? Data);

    private sealed record PancakeMessagingData(
        [property: JsonPropertyName("conversation")] PancakeWebhookConversation? Conversation,
        [property: JsonPropertyName("message")] PancakeWebhookMessage? Message,
        [property: JsonPropertyName("post")] PancakeWebhookPost? Post);

    private sealed record PancakeWebhookConversation(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("post_id")] string? PostId);

    private sealed record PancakeWebhookMessage(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("conversation_id")] string? ConversationId,
        [property: JsonPropertyName("page_id")] string? PageId,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("original_message")] string? OriginalMessage,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("inserted_at")] DateTime? InsertedAt,
        [property: JsonPropertyName("from")] PancakeWebhookParty? From);

    private sealed record PancakeWebhookParty(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record PancakeWebhookPost(
        [property: JsonPropertyName("id")] string? Id);
}

