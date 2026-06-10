namespace Clawbot.Api.Contracts.Security;

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    IReadOnlyList<string>? Scopes = null);

public sealed record CreateApiKeyRequest(
    string Name,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string>? Scopes = null);

public sealed record CreateApiKeyResponse(Guid Id, string Name, string PlaintextKey, DateTimeOffset? ExpiresAt);
