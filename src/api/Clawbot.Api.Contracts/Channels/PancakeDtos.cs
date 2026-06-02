namespace Clawbot.Api.Contracts.Channels;

public sealed record PancakeConfigDto(
    Guid Id,
    string BaseUrl,
    bool HasAccessToken,
    bool HasWebhookSecret,
    string SignatureHeader,
    string SignatureAlgo,
    string SignatureEncoding,
    string SendPathTemplate,
    string AuthMode,
    bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed record UpdatePancakeConfigRequest(
    string? BaseUrl,
    string? SendPathTemplate,
    string? AuthMode,
    string? SignatureHeader,
    string? SignatureAlgo,
    string? SignatureEncoding,
    string? AccessToken,
    string? WebhookSecret,
    bool? IsActive);

public sealed record PancakeWebhookUrlResponse(string WebhookUrl, string TenantSlug);
