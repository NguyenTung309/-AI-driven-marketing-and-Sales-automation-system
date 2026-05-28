using Clawbot.Domain.Common;

namespace Clawbot.Domain.ChatScenarios;

public sealed class ChatScenario : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;        // KB-001 .. KB-050
    public string GroupName { get; private set; } = string.Empty;
    public string TriggerText { get; private set; } = string.Empty;
    public string ResponseTemplate { get; private set; } = string.Empty;
    public string? ToneVoice { get; private set; }
    public string Platforms { get; private set; } = string.Empty;   // csv
    public decimal? SuccessRate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ChatScenario() { }

    public static ChatScenario Create(
        Guid tenantId,
        string code,
        string groupName,
        string triggerText,
        string responseTemplate,
        string platforms,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            GroupName = groupName,
            TriggerText = triggerText,
            ResponseTemplate = responseTemplate,
            Platforms = platforms,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
}
