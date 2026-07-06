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

        var row = await _db.Inboxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.ExternalPageId == pageId && i.DeletedAt == null && i.IsActive)
            .OrderBy(i => i.Id)
            .Select(i => new { i.EncryptedAccessToken, i.Name, i.Platform })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (row is null || string.IsNullOrEmpty(row.EncryptedAccessToken))
            return null;

        var token = PancakeTokenCipher.DecryptOrRaw(_encryptor, row.EncryptedAccessToken);
        if (string.IsNullOrEmpty(token))
            return null;

        return new PancakePageToken(token, pageId, row.Name, row.Platform);
    }

    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "PancakePageTokenResolver: requested tenant {requested} does not match ambient tenant {ambient}")]
    private static partial void LogTenantMismatch(ILogger logger, Guid requested, Guid ambient);
}