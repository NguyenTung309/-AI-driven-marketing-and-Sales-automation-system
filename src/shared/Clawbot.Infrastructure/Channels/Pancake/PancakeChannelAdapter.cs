using System.Text.Json;
using System.Text.Json.Serialization;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Security;
using Microsoft.Extensions.Configuration;

namespace Clawbot.Infrastructure.Channels.Pancake;

public sealed class PancakeChannelAdapter(HttpClient http, IConfiguration cfg) : IChannelAdapter
{
    private const string SignatureHeader = "x-pancake-signature";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = http;
    private readonly IConfiguration _cfg = cfg;

    public string Name => "pancake";

    public Task<bool> VerifyWebhookSignatureAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var secret = _cfg["Channels:Pancake:WebhookSecret"];
        if (string.IsNullOrEmpty(secret)) return Task.FromResult(false);

        if (!headers.TryGetValue(SignatureHeader, out var sig) || string.IsNullOrEmpty(sig))
            return Task.FromResult(false);

        return Task.FromResult(HmacSignatureVerifier.VerifyHexSha256(rawBody, sig, secret));
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
            if (!string.IsNullOrEmpty(evt.SenderName)) meta["display_name"] = evt.SenderName;

            list.Add(new ChannelMessage(
                Channel: evt.Platform ?? "pancake",
                ExternalThreadId: evt.ThreadId,
                ExternalUserId: evt.SenderId ?? string.Empty,
                Text: evt.Text,
                SentAt: evt.SentAt ?? DateTimeOffset.UtcNow,
                Metadata: meta));
        }
        return Task.FromResult<IReadOnlyList<ChannelMessage>>(list);
    }

    public async Task SendAsync(string externalThreadId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(externalThreadId)) throw new ArgumentException("thread id required", nameof(externalThreadId));
        var baseUrl = _cfg["Channels:Pancake:BaseUrl"] ?? "https://pages.fm";
        var token = _cfg["Channels:Pancake:AccessToken"];

        var payload = new SendBody(externalThreadId, text);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/v1/messages")
        {
            Content = System.Net.Http.Json.JsonContent.Create(payload, options: JsonOpts),
        };
        if (!string.IsNullOrEmpty(token)) req.Headers.Add("Authorization", $"Bearer {token}");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record SendBody(
        [property: JsonPropertyName("thread_id")] string ThreadId,
        [property: JsonPropertyName("text")] string Text);

    private sealed record PancakeWebhookPayload(
        [property: JsonPropertyName("events")] IReadOnlyList<PancakeEvent>? Events);

    private sealed record PancakeEvent(
        [property: JsonPropertyName("platform")] string? Platform,
        [property: JsonPropertyName("thread_id")] string? ThreadId,
        [property: JsonPropertyName("message_id")] string? MessageId,
        [property: JsonPropertyName("sender_id")] string? SenderId,
        [property: JsonPropertyName("sender_name")] string? SenderName,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("sent_at")] DateTimeOffset? SentAt);
}
