using Clawbot.Domain.Common;

namespace Clawbot.Domain.Channels;

// Per-page Pancake credential: a tenant connects one user access token (stored on AppUser) which can mint a
// page access token per page. Page tokens never expire (user token <=90d). One row per (tenant, page_id).
// SPEC-16 §5.1: page ops run under pages.fm/api/public_api/v1 with the page_access_token, NOT the user token.
public sealed class PancakePage : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string PageId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public string PageAccessTokenEncrypted { get; private set; } = string.Empty;
    public DateTimeOffset? PageTokenMintedAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private PancakePage() { }

    public static PancakePage Create(
        Guid tenantId,
        string pageId,
        string name,
        string platform,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PageId = pageId.Trim(),
            Name = name?.Trim() ?? string.Empty,
            Platform = platform?.Trim() ?? string.Empty,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    // EARS[WHEN a page access token is minted THE SYSTEM SHALL store it encrypted and stamp the mint time, never
    // persisting plaintext (CONSTITUTION secrets rule)]
    public void StorePageAccessToken(string encryptedToken, DateTimeOffset at)
    {
        PageAccessTokenEncrypted = encryptedToken ?? string.Empty;
        PageTokenMintedAt = at;
        UpdatedAt = at;
    }

    public void UpdateProfile(string name, string platform, DateTimeOffset at)
    {
        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(platform)) Platform = platform.Trim();
        UpdatedAt = at;
    }

    public void Activate(DateTimeOffset at) { IsActive = true; UpdatedAt = at; }
    public void Deactivate(DateTimeOffset at) { IsActive = false; UpdatedAt = at; }
}
