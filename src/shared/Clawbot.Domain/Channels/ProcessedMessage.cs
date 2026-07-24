using System.ComponentModel.DataAnnotations.Schema;

namespace Clawbot.Domain.Channels;

public sealed class ProcessedMessage : Clawbot.Domain.Common.IAuditExempt
{
    public Guid Id { get; private set; }

    [Column("tenant_id")]
    public Guid TenantId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string ExternalMessageId { get; private set; } = string.Empty;
    public string ConversationExternalId { get; private set; } = string.Empty;
    public DateTime ProcessedAt { get; private set; }

    private ProcessedMessage() { }

    public ProcessedMessage(Guid tenantId, string platform, string externalMessageId, string conversationExternalId)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Platform = platform;
        ExternalMessageId = externalMessageId;
        ConversationExternalId = conversationExternalId;
        ProcessedAt = DateTime.UtcNow;
    }
}
