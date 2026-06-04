using Clawbot.Domain.Common;

namespace Clawbot.Domain.Channels;

// pancake_configs — Pancake/omnichannel page credentials per tenant.
// Secrets are stored already-encrypted (see Infrastructure AesEncryptor); the domain never sees plaintext.
public sealed class PancakeConfig : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Channel { get; private set; } = string.Empty;          // zalo|facebook|instagram|...
    public string PageId { get; private set; } = string.Empty;
    public string AccessTokenEncrypted { get; private set; } = string.Empty;
    public string WebhookSecretEncrypted { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PancakeConfig() { }

    public static PancakeConfig Create(
        Guid tenantId,
        string channel,
        string pageId,
        string accessTokenEncrypted,
        string webhookSecretEncrypted,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel,
            PageId = pageId,
            AccessTokenEncrypted = accessTokenEncrypted,
            WebhookSecretEncrypted = webhookSecretEncrypted,
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void RotateSecrets(string accessTokenEncrypted, string webhookSecretEncrypted, DateTimeOffset updatedAt)
    {
        AccessTokenEncrypted = accessTokenEncrypted;
        WebhookSecretEncrypted = webhookSecretEncrypted;
        UpdatedAt = updatedAt;
    }

    public void Activate(DateTimeOffset updatedAt)   { IsActive = true;  UpdatedAt = updatedAt; }
    public void Deactivate(DateTimeOffset updatedAt) { IsActive = false; UpdatedAt = updatedAt; }
}
