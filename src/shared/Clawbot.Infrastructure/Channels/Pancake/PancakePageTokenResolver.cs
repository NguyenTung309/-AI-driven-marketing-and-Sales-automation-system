using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Channels.Pancake;

public sealed partial class PancakePageTokenResolver(
    AppDbContext db,
    IEncryptor encryptor,
    ITenantAccessor tenants,
    ILogger<PancakePageTokenResolver> logger) : IPancakePageTokenResolver
{
    private readonly AppDbContext _db = db;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly ITenantAccessor _tenants = tenants;
    private readonly ILogger<PancakePageTokenResolver> _logger = logger;

    public async Task<PancakePageToken?> ResolveAsync(Guid tenantId, string pageId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(pageId))
            return null;

        // Honor ambient tenant scope: never read another tenant's page token even if a caller passes a foreign id.
        if (_tenants.Current is { TenantId: var ambient } && ambient != tenantId)
        {
            LogTenantMismatch(_logger, tenantId, ambient);
            return null;
        }

        var row = await _db.PancakePages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.PageId == pageId && p.DeletedAt == null && p.IsActive)
            .Select(p => new { p.PageAccessTokenEncrypted, p.Name, p.Platform })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (row is null || string.IsNullOrEmpty(row.PageAccessTokenEncrypted))
            return null;

        var token = SafeDecrypt(row.PageAccessTokenEncrypted);
        if (string.IsNullOrEmpty(token))
            return null;

        return new PancakePageToken(token, pageId, row.Name, row.Platform);
    }

    private string SafeDecrypt(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return string.Empty;
        try { return _encryptor.Decrypt(cipher); }
        catch (FormatException) { return string.Empty; }
        catch (System.Security.Cryptography.CryptographicException) { return string.Empty; }
    }

    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "PancakePageTokenResolver: requested tenant {requested} does not match ambient tenant {ambient}")]
    private static partial void LogTenantMismatch(ILogger logger, Guid requested, Guid ambient);
}
