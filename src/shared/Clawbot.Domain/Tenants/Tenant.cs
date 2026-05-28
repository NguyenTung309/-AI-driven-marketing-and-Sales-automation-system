using Clawbot.Domain.Common;

namespace Clawbot.Domain.Tenants;

public sealed class Tenant : AggregateRoot<Guid>
{
    public string Slug { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = "free";
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private Tenant() { }

    public static Tenant Create(string slug, string displayName, string planName, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            DisplayName = displayName,
            PlanName = planName,
            IsActive = true,
            CreatedAt = createdAt,
        };
}
