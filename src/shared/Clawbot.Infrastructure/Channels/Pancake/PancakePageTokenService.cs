using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Channels.Pancake;

// Orchestrates per-page token lifecycle: returns a stored token if present, otherwise mints one via the gateway
// and persists it encrypted. The plaintext user access token is supplied by the caller (the admin connect flow,
// which reads it from AppUser) so this service stays decoupled from ASP.NET Identity.
public sealed partial class PancakePageTokenService(
    AppDbContext db,
    IEncryptor encryptor,
    IPancakePageTokenResolver resolver,
    IPageTokenMintGateway mintGateway,
    IClock clock,
    ILogger<PancakePageTokenService> logger) : IPancakePageTokenService
{
    private readonly AppDbContext _db = db;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly IPancakePageTokenResolver _resolver = resolver;
    private readonly IPageTokenMintGateway _mintGateway = mintGateway;
    private readonly IClock _clock = clock;
    private readonly ILogger<PancakePageTokenService> _logger = logger;

    // Returns a stored, decrypted page token, or null when the page is not connected / has no token yet.
    public Task<PancakePageToken?> EnsureMintedAsync(Guid tenantId, string pageId, CancellationToken ct = default) =>
        _resolver.ResolveAsync(tenantId, pageId, ct);

    // EARS[WHEN no stored page token exists for a (tenant, page) THE SYSTEM SHALL mint one from the user access
    // token, persist it encrypted, and return it; WHEN a row already exists THE SYSTEM SHALL overwrite its token
    // (minting invalidates the prior token) without creating a duplicate]
    public async Task<PancakePageToken> MintAndStoreAsync(
        Guid tenantId,
        string pageId,
        string name,
        string platform,
        string userAccessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAccessToken);

        var pageToken = await _mintGateway.MintAsync(userAccessToken, pageId, ct).ConfigureAwait(false);
        var encrypted = _encryptor.Encrypt(pageToken);
        var now = _clock.UtcNow;

        var existing = await _db.PancakePages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.PageId == pageId, ct).ConfigureAwait(false);

        if (existing is null)
        {
            var page = PancakePage.Create(tenantId, pageId, name, platform, now);
            page.StorePageAccessToken(encrypted, now);
            _db.PancakePages.Add(page);
        }
        else
        {
            existing.UpdateProfile(name, platform, now);
            existing.StorePageAccessToken(encrypted, now);
            if (!existing.IsActive) existing.Activate(now);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogMinted(_logger, pageId, existing is not null);
        return new PancakePageToken(pageToken, pageId, name?.Trim() ?? string.Empty, platform?.Trim() ?? string.Empty);
    }

    [LoggerMessage(EventId = 6011, Level = LogLevel.Information, Message = "Pancake page token minted+stored for page {pageId} (updated existing: {updatedExisting})")]
    private static partial void LogMinted(ILogger logger, string pageId, bool updatedExisting);

    // EARS[WHEN a page token is bootstrapped from an env var THE SYSTEM SHALL store it encrypted without minting]
    public async Task StorePageTokenDirectAsync(Guid tenantId, string pageId, string name, string platform, string pageAccessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageAccessToken);
        var encrypted = _encryptor.Encrypt(pageAccessToken);
        var now = _clock.UtcNow;

        var existing = await _db.PancakePages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.PageId == pageId, ct).ConfigureAwait(false);

        if (existing is null)
        {
            var page = PancakePage.Create(tenantId, pageId, name, platform, now);
            page.StorePageAccessToken(encrypted, now);
            _db.PancakePages.Add(page);
        }
        else
        {
            existing.UpdateProfile(name, platform, now);
            existing.StorePageAccessToken(encrypted, now);
            if (!existing.IsActive) existing.Activate(now);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogMinted(_logger, pageId, existing is not null);
    }
}

public interface IPancakePageTokenService
{
    Task<PancakePageToken?> EnsureMintedAsync(Guid tenantId, string pageId, CancellationToken ct = default);
    Task<PancakePageToken> MintAndStoreAsync(Guid tenantId, string pageId, string name, string platform, string userAccessToken, CancellationToken ct = default);
    // SPEC-16 Module M-1: store an already-minted page token (e.g. bootstrapped from an env var) encrypted,
    // without calling the mint gateway. Used by the startup bootstrap seeder so prod page tokens never live in appsettings.
    Task StorePageTokenDirectAsync(Guid tenantId, string pageId, string name, string platform, string pageAccessToken, CancellationToken ct = default);
}
