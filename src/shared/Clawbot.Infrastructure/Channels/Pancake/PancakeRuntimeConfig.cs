namespace Clawbot.Infrastructure.Channels.Pancake;

public sealed record PancakeRuntimeConfig(
    string BaseUrl,
    string AccessToken,
    string WebhookSecret,
    string SignatureHeader,
    string SignatureAlgo,
    string SignatureEncoding,
    string SendPathTemplate,
    string AuthMode);

public interface IPancakeConfigResolver
{
    Task<PancakeRuntimeConfig?> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
