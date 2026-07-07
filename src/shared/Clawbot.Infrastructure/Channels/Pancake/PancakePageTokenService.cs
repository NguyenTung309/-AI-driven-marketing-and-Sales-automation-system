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
    // Tokens land on the inbox row (inboxes.encrypted_access_token) — the single per-channel store that both
    // polling (inbound) and the channel adapter (outbound) read.
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
        var updatedExisting = await UpsertInboxTokenAsync(tenantId, pageId, name, platform, _encryptor.Encrypt(pageToken), ct).ConfigureAwait(false);
        LogMinted(_logger, pageId, updatedExisting);
        return new PancakePageToken(pageToken, pageId, name?.Trim() ?? string.Empty, platform?.Trim() ?? string.Empty);
    }

    [LoggerMessage(EventId = 6011, Level = LogLevel.Information, Message = "Pancake page token minted+stored for page {pageId} (updated existing: {updatedExisting})")]
    private static partial void LogMinted(ILogger logger, string pageId, bool updatedExisting);

    // EARS[WHEN a page token is bootstrapped from an env var THE SYSTEM SHALL store it encrypted without minting]
    public async Task StorePageTokenDirectAsync(Guid tenantId, string pageId, string name, string platform, string pageAccessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageAccessToken);
        var updatedExisting = await UpsertInboxTokenAsync(tenantId, pageId, name, platform, _encryptor.Encrypt(pageAccessToken), ct).ConfigureAwait(false);
        LogMinted(_logger, pageId, updatedExisting);
    }

    private async Task<bool> UpsertInboxTokenAsync(Guid tenantId, string pageId, string name, string platform, string encryptedToken, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var existing = await _db.Inboxes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.ExternalPageId == pageId && i.DeletedAt == null, ct).ConfigureAwait(false);

        if (existing is null)
        {
            var inbox = Inbox.Create(tenantId, string.IsNullOrWhiteSpace(name) ? pageId : name.Trim(), platform, pageId);
            inbox.SetAccessToken(encryptedToken, now);
            _db.Inboxes.Add(inbox);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(name)) existing.UpdateName(name.Trim(), now);
            existing.SetAccessToken(encryptedToken, now);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing is not null;
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
