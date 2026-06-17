using Clawbot.Domain.Common;

namespace Clawbot.Domain.Experiments;

public sealed class ExperimentVariant : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ExperimentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Weight { get; private set; }
    public Guid? ChatScenarioId { get; private set; }
    public Guid? KbVersionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ExperimentVariant() { }

    internal static ExperimentVariant Create(
        Guid tenantId,
        Guid experimentId,
        string code,
        string name,
        int weight,
        Guid? chatScenarioId,
        Guid? kbVersionId,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExperimentId = experimentId,
            Code = code.Trim(),
            Name = name.Trim(),
            Weight = weight,
            ChatScenarioId = chatScenarioId,
            KbVersionId = kbVersionId,
            CreatedAt = createdAt,
        };
}
