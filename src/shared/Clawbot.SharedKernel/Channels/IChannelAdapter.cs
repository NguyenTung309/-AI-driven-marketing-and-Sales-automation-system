namespace Clawbot.SharedKernel.Channels;

public interface IChannelAdapter
{
    string Name { get; }
    Task<bool> VerifyWebhookSignatureAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default);
    /// <returns>Message id phia kenh (vd Pancake send response id) de dedup echo; null khi kenh khong tra id.</returns>
    Task<string?> SendAsync(string externalThreadId, string text, CancellationToken ct = default);
    Task<string?> SendAsync(string externalThreadId, string text, string? accessToken, CancellationToken ct = default) =>
        SendAsync(externalThreadId, text, ct);
}
