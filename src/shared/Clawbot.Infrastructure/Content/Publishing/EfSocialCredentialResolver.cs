using System.Text.Json;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Content.Publishing;

// SPEC-16 Module M-1: resolves a tenant's social channel credentials from the encrypted DB store
// (social_credentials), falling back to options-based config when no DB row exists. GraphSocialPublisher uses
// this so production creds live encrypted in the DB, not in appsettings.json.
public sealed partial class EfSocialCredentialResolver(
    AppDbContext db,
    IEncryptor encryptor,
    ITenantAccessor tenants,
    ILogger<EfSocialCredentialResolver> logger) : ISocialCredentialResolver, IInstagramCredentialResolver
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly ITenantAccessor _tenants = tenants;
    private readonly ILogger<EfSocialCredentialResolver> _logger = logger;

    public async Task<GraphChannelOptions?> ResolveAsync(Guid tenantId, string provider, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(provider)) return null;
        // Honor ambient tenant scope: never read another tenant's credentials.
        if (_tenants.Current is { TenantId: var ambient } && ambient != tenantId)
        {
            LogTenantMismatch(_logger, tenantId, ambient);
            return null;
        }

        var normalized = provider.Trim().ToLowerInvariant();
        if (string.Equals(normalized, "instagram", StringComparison.Ordinal))
        {
            var resolution = await ResolveAsync(tenantId, ct).ConfigureAwait(false);
            return resolution.Status switch
            {
                InstagramCredentialResolutionStatus.Disabled => new GraphChannelOptions(),
                InstagramCredentialResolutionStatus.Resolved when resolution.Credential is not null => new GraphChannelOptions
                {
                    Enabled = true,
                    PageId = resolution.Credential.InstagramUserId,
                    PageAccessToken = resolution.Credential.AccessToken,
                },
                _ => null,
            };
        }

        var row = await _db.SocialCredentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Provider == normalized && c.DeletedAt == null && c.IsActive)
            // ponytail: prefer a page-specific row, else the tenant-wide row (page_id null).
            .OrderBy(c => c.PageId == null ? 1 : 0)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (row is null) return null;
        var json = SafeDecrypt(row.CredentialsEncrypted);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var options = JsonSerializer.Deserialize<GraphChannelOptions>(json, JsonOpts);
            return options is null ? null : InstagramCredentialEnvelopeCodec.Normalize(options);
        }
        catch (JsonException ex)
        {
            LogParseFailed(_logger, ex, tenantId, normalized);
            return null;
        }
    }

    public async Task<InstagramCredentialResolution> ResolveAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return new InstagramCredentialResolution(InstagramCredentialResolutionStatus.Invalid);
        if (_tenants.Current is { TenantId: var ambient } && ambient != tenantId)
        {
            LogTenantMismatch(_logger, tenantId, ambient);
            return new InstagramCredentialResolution(InstagramCredentialResolutionStatus.Invalid);
        }

        var row = await _db.SocialCredentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                credential => credential.TenantId == tenantId
                    && credential.Provider == "instagram"
                    && credential.PageId == null
                    && credential.DeletedAt == null,
                ct)
            .ConfigureAwait(false);
        if (row is null)
            return new InstagramCredentialResolution(InstagramCredentialResolutionStatus.Absent);
        if (!row.IsActive)
            return new InstagramCredentialResolution(InstagramCredentialResolutionStatus.Invalid);

        var decoded = InstagramCredentialEnvelopeCodec.Decode(
            _encryptor,
            tenantId,
            "instagram",
            row.PageId,
            row.CredentialsEncrypted);
        if (decoded.Status == InstagramCredentialEnvelopeStatus.Invalid || decoded.Options is null)
            return new InstagramCredentialResolution(InstagramCredentialResolutionStatus.Invalid);

        var options = decoded.Options;
        if (!options.Enabled)
            return new InstagramCredentialResolution(InstagramCredentialResolutionStatus.Disabled);
        if (!IsNumericInstagramUserId(options.PageId)
            || string.IsNullOrWhiteSpace(options.PageAccessToken))
        {
            return new InstagramCredentialResolution(InstagramCredentialResolutionStatus.Invalid);
        }

        return new InstagramCredentialResolution(
            InstagramCredentialResolutionStatus.Resolved,
            new InstagramCredential(options.PageId, options.PageAccessToken));
    }

    private static bool IsNumericInstagramUserId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(char.IsAsciiDigit);

    private string SafeDecrypt(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return string.Empty;
        try { return _encryptor.Decrypt(cipher); }
        catch (FormatException) { return string.Empty; }
        catch (System.Security.Cryptography.CryptographicException) { return string.Empty; }
    }

    [LoggerMessage(EventId = 5210, Level = LogLevel.Warning, Message = "SocialCredentialResolver: requested tenant {requested} does not match ambient {ambient}")]
    private static partial void LogTenantMismatch(ILogger logger, Guid requested, Guid ambient);

    [LoggerMessage(EventId = 5211, Level = LogLevel.Error, Message = "SocialCredentialResolver: failed to parse decrypted credentials for tenant {tenantId} provider {provider}")]
    private static partial void LogParseFailed(ILogger logger, Exception ex, Guid tenantId, string provider);

    [LoggerMessage(EventId = 5212, Level = LogLevel.Error, Message = "SocialCredentialResolver: failed to decrypt credentials for tenant {tenantId} provider {provider}")]
    private static partial void LogDecryptFailed(ILogger logger, Exception ex, Guid tenantId, string provider);
}

public interface ISocialCredentialResolver
{
    Task<GraphChannelOptions?> ResolveAsync(Guid tenantId, string provider, CancellationToken ct = default);
}
