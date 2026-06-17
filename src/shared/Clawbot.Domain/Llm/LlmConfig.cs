using Clawbot.Domain.Common;

namespace Clawbot.Domain.Llm;

// llm_configs — per-tenant LLM provider credentials & generation defaults.
// api_key stored already-encrypted; the domain never holds plaintext.
public sealed class LlmConfig : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Provider { get; private set; } = string.Empty;     // anthropic|openai|...
    public string ModelId { get; private set; } = string.Empty;
    public string ApiKeyEncrypted { get; private set; } = string.Empty;
    public string? BaseUrl { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int? MaxTokens { get; private set; }
    public decimal? Temperature { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private LlmConfig() { }

    public static LlmConfig Create(
        Guid tenantId,
        string provider,
        string modelId,
        string apiKeyEncrypted,
        DateTimeOffset createdAt,
        string? baseUrl = null,
        int? maxTokens = null,
        decimal? temperature = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider,
            ModelId = modelId,
            ApiKeyEncrypted = apiKeyEncrypted,
            BaseUrl = baseUrl,
            IsActive = true,
            MaxTokens = maxTokens,
            Temperature = temperature,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void UpdateDefaults(int? maxTokens, decimal? temperature, DateTimeOffset updatedAt)
    {
        MaxTokens = maxTokens;
        Temperature = temperature;
        UpdatedAt = updatedAt;
    }

    public void RotateApiKey(string apiKeyEncrypted, DateTimeOffset updatedAt)
    {
        ApiKeyEncrypted = apiKeyEncrypted;
        UpdatedAt = updatedAt;
    }

    public void Activate(DateTimeOffset updatedAt)   { IsActive = true;  UpdatedAt = updatedAt; }
    public void Deactivate(DateTimeOffset updatedAt) { IsActive = false; UpdatedAt = updatedAt; }
}
