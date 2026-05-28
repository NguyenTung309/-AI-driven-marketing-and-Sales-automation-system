using Clawbot.Domain.Common;

namespace Clawbot.Domain.KnowledgeBase;

public sealed class KbModule : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<KbVersion> _versions = new();
    private readonly List<KbTestCase> _testCases = new();

    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? OwnerRole { get; private set; }
    public string Status { get; private set; } = "active";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<KbVersion> Versions => _versions.AsReadOnly();
    public IReadOnlyCollection<KbTestCase> TestCases => _testCases.AsReadOnly();

    private KbModule() { }

    public static KbModule Create(Guid tenantId, string code, string name, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            CreatedAt = createdAt,
        };
}
