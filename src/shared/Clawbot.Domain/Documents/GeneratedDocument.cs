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
    // Docs-1: download link expires 7 days after generation.
    public DateTimeOffset? ExpiresAt { get; private set; }

    // Docs-1: quote/brochure download links are valid for 7 days.
    public const int LinkValidityDays = 7;

    private GeneratedDocument() { }

    public static GeneratedDocument Create(
        Guid tenantId,
        Guid templateId,
        string fileUrl,
        DateTimeOffset createdAt,
        Guid? contactId = null,
        Guid? generatedBy = null,
        string? fileHash = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TemplateId = templateId,
            FileUrl = fileUrl,
            ContactId = contactId,
            GeneratedBy = generatedBy,
            FileHash = fileHash,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddDays(LinkValidityDays),
        };

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is not null && now > ExpiresAt.Value;

    public void MarkSent(string sentVia, DateTimeOffset at)
    {
        SentVia = sentVia;
        SentAt = at;
    }

    public void MarkOpened(DateTimeOffset at) => OpenedAt = at;
}
