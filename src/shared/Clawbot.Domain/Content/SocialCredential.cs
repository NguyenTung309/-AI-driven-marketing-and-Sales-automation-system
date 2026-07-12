using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

// SPEC-16 Module M-1: encrypted storage for social channel credentials (FB app id/secret + page tokens + scopes,
// Zalo OA token/refresh + app secret). One row per (tenant, provider); the credential payload is a single
// encrypted JSON blob (IEncryptor) so the schema stays provider-agnostic without a wide column set per platform.
// The plaintext never leaves this aggregate; consumers decrypt via ISocialCredentialResolver.
public sealed class SocialCredential : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Provider { get; private set; } = string.Empty;   // meta | facebook | zalo
    public string? PageId { get; private set; }                    // optional: per-page credential (e.g. FB page token)
    public string CredentialsEncrypted { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private SocialCredential() { }

    public static SocialCredential Create(Guid tenantId, string provider, string encrypted, DateTimeOffset createdAt, string? pageId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider.Trim().ToLowerInvariant(),
            PageId = string.IsNullOrWhiteSpace(pageId) ? null : pageId.Trim(),
            CredentialsEncrypted = encrypted ?? string.Empty,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    // EARS[WHEN credentials are stored THE SYSTEM SHALL keep only the encrypted blob (CONSTITUTION secrets rule)]
    public void UpdateCredentials(string encrypted, DateTimeOffset at)
    {
        CredentialsEncrypted = encrypted ?? string.Empty;
        UpdatedAt = at;
    }

    public void Deactivate(DateTimeOffset at) { IsActive = false; UpdatedAt = at; }
    public void Activate(DateTimeOffset at) { IsActive = true; UpdatedAt = at; }
}
