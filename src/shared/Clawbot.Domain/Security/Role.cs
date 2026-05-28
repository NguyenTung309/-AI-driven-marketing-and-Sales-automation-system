using Clawbot.Domain.Common;

namespace Clawbot.Domain.Security;

public sealed class Role : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Role() { }

    public static Role Create(Guid tenantId, string name, string? description, bool isSystem, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            IsSystem = isSystem,
            CreatedAt = createdAt,
        };
}
