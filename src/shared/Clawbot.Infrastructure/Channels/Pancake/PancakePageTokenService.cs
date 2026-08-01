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
    public Task<PancakePageToken?> EnsureMintedAsync(
        Guid tenantId,
        string platform,
        string pageId,
        CancellationToken ct = default) =>
        _resolver.ResolveAsync(tenantId, platform, pageId, ct);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(userAccessToken);
        var identity = NormalizeIdentity(tenantId, platform, pageId);
        var normalizedName = NormalizeName(name, identity.PageId);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquirePageTokenLockAsync(identity, ct).ConfigureAwait(false);
        var pageToken = await _mintGateway
            .MintAsync(userAccessToken, identity.PageId, ct)
            .ConfigureAwait(false);
        var updatedExisting = await UpsertInboxTokenAsync(
            identity,
            normalizedName,
            _encryptor.Encrypt(pageToken),
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        LogMinted(_logger, identity.PageId, updatedExisting);
        return new PancakePageToken(
            pageToken,
            identity.PageId,
            normalizedName,
            identity.Platform);
    }

    [LoggerMessage(EventId = 6011, Level = LogLevel.Information, Message = "Pancake page token minted+stored for page {pageId} (updated existing: {updatedExisting})")]
    private static partial void LogMinted(ILogger logger, string pageId, bool updatedExisting);

    // EARS[WHEN a page token is bootstrapped from an env var THE SYSTEM SHALL store it encrypted without minting]
    public async Task StorePageTokenDirectAsync(Guid tenantId, string pageId, string name, string platform, string pageAccessToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageAccessToken);
        var identity = NormalizeIdentity(tenantId, platform, pageId);
        var normalizedName = NormalizeName(name, identity.PageId);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquirePageTokenLockAsync(identity, ct).ConfigureAwait(false);
        var updatedExisting = await UpsertInboxTokenAsync(
            identity,
            normalizedName,
            _encryptor.Encrypt(pageAccessToken),
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        LogMinted(_logger, identity.PageId, updatedExisting);
    }

    private async Task<bool> UpsertInboxTokenAsync(
        CanonicalPageIdentity identity,
        string normalizedName,
        string encryptedToken,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var matches = await _db.Inboxes
            .IgnoreQueryFilters()
            .Where(inbox => inbox.TenantId == identity.TenantId
                && inbox.Platform == identity.Platform
                && inbox.ExternalPageId == identity.PageId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existing = matches
            .OrderByDescending(inbox => inbox.IsActive && inbox.DeletedAt == null)
            .ThenBy(inbox => inbox.CreatedAt)
            .ThenBy(inbox => inbox.Id)
            .FirstOrDefault();

        if (existing is null)
        {
            var inbox = Inbox.Create(
                identity.TenantId,
                normalizedName,
                identity.Platform,
                identity.PageId);
            inbox.SetAccessToken(encryptedToken, now);
            _db.Inboxes.Add(inbox);
        }
        else
        {
            existing.Reconnect(normalizedName, encryptedToken, now);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existing is not null;
    }

    private async Task AcquirePageTokenLockAsync(
        CanonicalPageIdentity identity,
        CancellationToken ct)
    {
        if (!_db.Database.IsSqlServer())
            return;

        var resource = $"clawbot:pancake-page-token:{identity.TenantId:N}:{identity.Platform}:{identity.PageId}";
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource={resource},
                @LockMode='Exclusive',
                @LockOwner='Transaction',
                @LockTimeout=15000;
            IF @result < 0
                THROW 51000, 'pancake_page_token_lock_failed', 1;
            """, ct).ConfigureAwait(false);
    }

    private static CanonicalPageIdentity NormalizeIdentity(
        Guid tenantId,
        string platform,
        string pageId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        if (normalizedPlatform.Length > 32)
            throw new ArgumentOutOfRangeException(nameof(platform), "Platform must not exceed 32 characters.");
        var normalizedPageId = pageId.Trim();
        if (normalizedPageId.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(pageId), "Page ID must not exceed 128 characters.");

        return new CanonicalPageIdentity(tenantId, normalizedPlatform, normalizedPageId);
    }

    private static string NormalizeName(string name, string pageId) =>
        string.IsNullOrWhiteSpace(name) ? pageId : name.Trim();

    private sealed record CanonicalPageIdentity(Guid TenantId, string Platform, string PageId);
}

public interface IPancakePageTokenService
{
    Task<PancakePageToken?> EnsureMintedAsync(
        Guid tenantId,
        string platform,
        string pageId,
        CancellationToken ct = default);
    Task<PancakePageToken> MintAndStoreAsync(Guid tenantId, string pageId, string name, string platform, string userAccessToken, CancellationToken ct = default);
    // SPEC-16 Module M-1: store an already-minted page token (e.g. bootstrapped from an env var) encrypted,
    // without calling the mint gateway. Used by the startup bootstrap seeder so prod page tokens never live in appsettings.
    Task StorePageTokenDirectAsync(Guid tenantId, string pageId, string name, string platform, string pageAccessToken, CancellationToken ct = default);
}
