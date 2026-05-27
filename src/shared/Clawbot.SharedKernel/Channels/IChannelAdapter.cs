namespace Clawbot.SharedKernel.Channels;

public interface IChannelAdapter
{
    string Name { get; }
    Task<bool> VerifyWebhookSignatureAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default);
    Task SendAsync(string externalThreadId, string text, CancellationToken ct = default);
}
