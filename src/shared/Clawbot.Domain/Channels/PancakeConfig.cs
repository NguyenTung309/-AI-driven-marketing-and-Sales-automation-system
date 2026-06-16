using Clawbot.Domain.Common;

namespace Clawbot.Domain.Channels;

public sealed class PancakeConfig : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string BaseUrl { get; private set; } = "https://pancake.vn/api/v1";
    public string AccessTokenEncrypted { get; private set; } = string.Empty;
    public string WebhookSecretEncrypted { get; private set; } = string.Empty;
    public string SignatureHeader { get; private set; } = "x-pancake-signature";
    public string SignatureAlgo { get; private set; } = "hmac-sha256";
    public string SignatureEncoding { get; private set; } = "hex";
    public string SendPathTemplate { get; private set; } =
        "/pages/{page_id}/conversations/{thread_id}/messages";
    public string PageId { get; private set; } = string.Empty;
    public string AuthMode { get; private set; } = "query";
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PancakeConfig() { }

    public static PancakeConfig Create(Guid tenantId, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void UpdateEndpoint(string baseUrl, string sendPathTemplate, string authMode, DateTimeOffset at)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl)) BaseUrl = baseUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(sendPathTemplate)) SendPathTemplate = sendPathTemplate;
        if (!string.IsNullOrWhiteSpace(authMode)) AuthMode = authMode;
        UpdatedAt = at;
    }

    public void UpdateSignature(string header, string algo, string encoding, DateTimeOffset at)
    {
        if (!string.IsNullOrWhiteSpace(header)) SignatureHeader = header.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(algo)) SignatureAlgo = algo.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(encoding)) SignatureEncoding = encoding.ToLowerInvariant();
        UpdatedAt = at;
    }

    public void UpdateAccessToken(string encrypted, DateTimeOffset at)
    {
        AccessTokenEncrypted = encrypted ?? string.Empty;
        UpdatedAt = at;
    }

    public void UpdateWebhookSecret(string encrypted, DateTimeOffset at)
    {
        WebhookSecretEncrypted = encrypted ?? string.Empty;
        UpdatedAt = at;
    }

    public void Activate(DateTimeOffset at) { IsActive = true; UpdatedAt = at; }
    public void Deactivate(DateTimeOffset at) { IsActive = false; UpdatedAt = at; }
}
