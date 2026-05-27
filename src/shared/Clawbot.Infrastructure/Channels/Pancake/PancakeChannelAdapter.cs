using Clawbot.SharedKernel.Channels;

namespace Clawbot.Infrastructure.Channels.Pancake;

public sealed class PancakeChannelAdapter(HttpClient http) : IChannelAdapter
{
    public string Name => "pancake";

    public Task<bool> VerifyWebhookSignatureAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        // TODO(student): implement HMAC verification using shared secret.
        _ = rawBody;
        _ = headers;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default)
    {
        // TODO(student): map Pancake webhook payload -> ChannelMessage[].
        _ = rawBody;
        return Task.FromResult<IReadOnlyList<ChannelMessage>>(Array.Empty<ChannelMessage>());
    }

    public Task SendAsync(string externalThreadId, string text, CancellationToken ct = default)
    {
        // TODO(student): POST to Pancake API via injected HttpClient.
        _ = http;
        _ = externalThreadId;
        _ = text;
        return Task.CompletedTask;
    }
}
