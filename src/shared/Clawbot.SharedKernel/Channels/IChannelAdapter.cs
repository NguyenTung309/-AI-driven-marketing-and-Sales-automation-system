namespace Clawbot.SharedKernel.Channels;

public sealed class ChannelSendRejectedException(string code, int? statusCode = null)
    : Exception(code)
{
    public string Code { get; } = code;
    public int? StatusCode { get; } = statusCode;
}

public sealed class ChannelDeliveryAmbiguousException(string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public string Code { get; } = code;
}

public interface IChannelAdapter
{
    string Name { get; }
    Task<bool> VerifyWebhookSignatureAsync(Guid tenantId, string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default);
    /// <returns>Message id phia kenh (vd Pancake send response id) de dedup echo; null khi kenh khong tra id.</returns>
    Task<string?> SendAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string text,
        CancellationToken ct = default);

    Task<string?> SendAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string text,
        string? accessToken,
        CancellationToken ct = default) =>
        SendAsync(tenantId, platform, externalThreadId, text, ct);
}
