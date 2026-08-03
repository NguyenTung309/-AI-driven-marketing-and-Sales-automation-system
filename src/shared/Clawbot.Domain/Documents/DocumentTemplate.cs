using Clawbot.Domain.Common;

namespace Clawbot.Domain.Documents;

public sealed class DocumentTemplate : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DocType { get; private set; } = string.Empty;   // quote|brochure|slide|onboarding
    public string TemplateHtml { get; private set; } = string.Empty;
    public string FieldsJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private DocumentTemplate() { }

    public static DocumentTemplate Create(Guid tenantId, string code, string docType, string templateHtml, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            DocType = docType,
            TemplateHtml = templateHtml,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
}
