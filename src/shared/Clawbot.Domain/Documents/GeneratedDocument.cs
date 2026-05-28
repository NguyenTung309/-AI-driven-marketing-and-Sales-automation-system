using Clawbot.Domain.Common;

namespace Clawbot.Domain.Documents;

public sealed class GeneratedDocument : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid TemplateId { get; private set; }
    public Guid? GeneratedBy { get; private set; }
    public string FileUrl { get; private set; } = string.Empty;
    public string? FileHash { get; private set; }
    public string? SentVia { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private GeneratedDocument() { }

    public static GeneratedDocument Create(
        Guid tenantId,
        Guid templateId,
        string fileUrl,
        DateTimeOffset createdAt,
        Guid? contactId = null,
        Guid? generatedBy = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TemplateId = templateId,
            FileUrl = fileUrl,
            ContactId = contactId,
            GeneratedBy = generatedBy,
            CreatedAt = createdAt,
        };

    public void MarkSent(string sentVia, DateTimeOffset at)
    {
        SentVia = sentVia;
        SentAt = at;
    }

    public void MarkOpened(DateTimeOffset at) => OpenedAt = at;
}
