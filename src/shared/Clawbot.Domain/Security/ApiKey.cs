using Clawbot.Domain.Common;

namespace Clawbot.Domain.Security;

public sealed class ApiKey : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string KeyHash { get; private set; } = string.Empty;
    public string ScopesJson { get; private set; } = "[]";
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ApiKey() { }

    public static ApiKey Issue(Guid tenantId, string name, string keyHash, DateTimeOffset createdAt, DateTimeOffset? expiresAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            KeyHash = keyHash,
            ExpiresAt = expiresAt,
            CreatedAt = createdAt,
        };

    public void Revoke(DateTimeOffset at) => RevokedAt = at;
}
