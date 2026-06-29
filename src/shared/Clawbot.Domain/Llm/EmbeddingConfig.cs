using Clawbot.Domain.Common;

namespace Clawbot.Domain.Llm;

// embedding_configs — per-tenant vector embedding provider credentials for KB/RAG.
// api_key stored already-encrypted; the domain never holds plaintext.
public sealed class EmbeddingConfig : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Provider { get; private set; } = string.Empty; // openai|openai-compatible|hash
    public string ModelId { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public string ApiKeyEncrypted { get; private set; } = string.Empty;
    public string? BaseUrl { get; private set; }
    public int Dimension { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private EmbeddingConfig() { }

    public static EmbeddingConfig Create(
        Guid tenantId,
        string provider,
        string modelId,
        string apiKeyEncrypted,
        int dimension,
        DateTimeOffset createdAt,
        string? baseUrl = null,
        string? displayName = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider,
            ModelId = modelId,
            DisplayName = displayName,
            ApiKeyEncrypted = apiKeyEncrypted,
            BaseUrl = baseUrl,
            Dimension = dimension,
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void UpdateConnection(string provider, string modelId, string? baseUrl, string? displayName, int dimension, DateTimeOffset updatedAt)
    {
        Provider = provider;
        ModelId = modelId;
        BaseUrl = baseUrl;
        DisplayName = displayName;
        Dimension = dimension;
        UpdatedAt = updatedAt;
    }

    public void RotateApiKey(string apiKeyEncrypted, DateTimeOffset updatedAt)
    {
        ApiKeyEncrypted = apiKeyEncrypted;
        UpdatedAt = updatedAt;
    }

    public void RequireKeyRotation(DateTimeOffset updatedAt)
    {
        ApiKeyEncrypted = string.Empty;
        IsActive = false;
        UpdatedAt = updatedAt;
    }

    public void Activate(DateTimeOffset updatedAt)
    {
        if (!Provider.Equals("hash", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(ApiKeyEncrypted))
            throw new InvalidOperationException("Embedding config requires key rotation before activation.");
        IsActive = true;
        UpdatedAt = updatedAt;
    }

    public void Deactivate(DateTimeOffset updatedAt)
    {
        IsActive = false;
        UpdatedAt = updatedAt;
    }
}
