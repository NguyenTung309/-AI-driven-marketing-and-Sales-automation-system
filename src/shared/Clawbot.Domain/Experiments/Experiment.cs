using Clawbot.Domain.Common;

namespace Clawbot.Domain.Experiments;

public sealed class Experiment : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<ExperimentVariant> _variants = new();

    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid TargetId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = "active";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<ExperimentVariant> Variants => _variants.AsReadOnly();

    private Experiment() { }

    public static Experiment Create(
        Guid tenantId,
        string code,
        string targetType,
        Guid targetId,
        string name,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code.Trim(),
            TargetType = targetType.Trim().ToLowerInvariant(),
            TargetId = targetId,
            Name = name.Trim(),
            CreatedAt = createdAt,
        };

    public ExperimentVariant AddVariant(
        string code,
        string name,
        int weight,
        Guid? chatScenarioId,
        Guid? kbVersionId,
        DateTimeOffset createdAt)
    {
        if (weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight), "Variant weight must be positive.");
        var variant = ExperimentVariant.Create(TenantId, Id, code, name, weight, chatScenarioId, kbVersionId, createdAt);
        _variants.Add(variant);
        UpdatedAt = createdAt;
        return variant;
    }

    public void Stop(DateTimeOffset at)
    {
        Status = "stopped";
        UpdatedAt = at;
    }
}
