namespace Clawbot.Api.Contracts.Security;

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

public sealed record CreateApiKeyRequest(string Name, DateTimeOffset? ExpiresAt);

public sealed record CreateApiKeyResponse(Guid Id, string Name, string PlaintextKey, DateTimeOffset? ExpiresAt);
