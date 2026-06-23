namespace Clawbot.Domain.ChatScenarios;

using Clawbot.Domain.Common;

public sealed class Label : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "#6366f1";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Label() { }

    public static Label Create(Guid tenantId, string name, string color)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Color = color,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    public void Update(string name, string color)
    {
        Name = name;
        Color = color;
    }

    public void SoftDelete() => DeletedAt = DateTimeOffset.UtcNow;
}