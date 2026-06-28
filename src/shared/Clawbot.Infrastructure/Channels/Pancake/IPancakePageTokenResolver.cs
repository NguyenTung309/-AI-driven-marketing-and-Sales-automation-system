using Clawbot.SharedKernel.Security;

namespace Clawbot.Infrastructure.Channels.Pancake;

// Resolved per-page Pancake credential: the decrypted page access token plus identity for routing/audit.
public sealed record PancakePageToken(string PageAccessToken, string PageId, string Name, string Platform);

// Reads a tenant's stored page access token (minted by PancakePageTokenService). Returns null when the page is
// not connected or has no stored token yet (caller mints on demand).
public interface IPancakePageTokenResolver
{
    Task<PancakePageToken?> ResolveAsync(Guid tenantId, string pageId, CancellationToken ct = default);
}
