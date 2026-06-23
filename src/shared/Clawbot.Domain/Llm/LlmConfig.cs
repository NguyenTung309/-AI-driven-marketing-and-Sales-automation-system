using Clawbot.Domain.Common;

namespace Clawbot.Domain.Llm;

// llm_configs — per-tenant LLM provider credentials & generation defaults.
// api_key stored already-encrypted; the domain never holds plaintext.
public sealed class LlmConfig : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Provider { get; private set; } = string.Empty;     // anthropic|openai|...
    public string ModelId { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }                  // admin label for the picker
    public string ApiKeyEncrypted { get; private set; } = string.Empty;
    public string? BaseUrl { get; private set; }
    public bool IsActive { get; private set; } = true;
    public decimal? InputUsdPer1M { get; private set; }              // cost rate; null → provider default
    public decimal? OutputUsdPer1M { get; private set; }
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
        string? displayName = null,
        decimal? inputUsdPer1M = null,
        decimal? outputUsdPer1M = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = provider,
            ModelId = modelId,
            DisplayName = displayName,
            ApiKeyEncrypted = apiKeyEncrypted,
            BaseUrl = baseUrl,
            IsActive = true,
            InputUsdPer1M = inputUsdPer1M,
            OutputUsdPer1M = outputUsdPer1M,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    // Update connection identity (provider/model/baseUrl/label) without touching the key.
    public void UpdateConnection(string provider, string modelId, string? baseUrl, string? displayName, DateTimeOffset updatedAt)
    {
        Provider = provider;
        ModelId = modelId;
        BaseUrl = baseUrl;
        DisplayName = displayName;
        UpdatedAt = updatedAt;
    }

    public void UpdateRates(decimal? inputUsdPer1M, decimal? outputUsdPer1M, DateTimeOffset updatedAt)
    {
        InputUsdPer1M = inputUsdPer1M;
        OutputUsdPer1M = outputUsdPer1M;
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
        if (string.IsNullOrWhiteSpace(ApiKeyEncrypted))
            throw new InvalidOperationException("LLM config requires key rotation before activation.");
        IsActive = true;
        UpdatedAt = updatedAt;
    }

    public void Deactivate(DateTimeOffset updatedAt) { IsActive = false; UpdatedAt = updatedAt; }
}
