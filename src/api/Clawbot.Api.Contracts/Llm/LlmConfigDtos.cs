namespace Clawbot.Api.Contracts.Llm;

// Response — API key is never returned; presence is exposed via HasApiKey (mirrors ChannelsEndpoints).
public sealed record LlmConfigDto(
    Guid Id,
    string Provider,
    string ModelId,
    string? DisplayName,
    bool HasApiKey,
    string? BaseUrl,
    bool IsActive,
    decimal? InputUsdPer1M,
    decimal? OutputUsdPer1M,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? TimeoutSeconds = null,
    int? MaxOutputTokens = null,
    bool? SupportsVision = null);

public sealed record CreateLlmConfigRequest(
    string Provider,
    string ModelId,
    string ApiKey,
    string? DisplayName = null,
    string? BaseUrl = null,
    decimal? InputUsdPer1M = null,
    decimal? OutputUsdPer1M = null,
    int? TimeoutSeconds = null,
    int? MaxOutputTokens = null,
    bool? SupportsVision = null);

// Update never carries the key — rotate it via the dedicated endpoint.
public sealed record UpdateLlmConfigRequest(
    string Provider,
    string ModelId,
    string? DisplayName = null,
    string? BaseUrl = null,
    decimal? InputUsdPer1M = null,
    decimal? OutputUsdPer1M = null,
    int? TimeoutSeconds = null,
    int? MaxOutputTokens = null,
    bool? SupportsVision = null);

public sealed record RotateLlmKeyRequest(string ApiKey);

public sealed record TestLlmConfigResponse(bool Ok, long LatencyMs, string? Error = null);
