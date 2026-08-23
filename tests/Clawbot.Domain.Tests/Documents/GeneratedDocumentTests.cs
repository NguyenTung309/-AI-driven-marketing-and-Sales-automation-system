using Clawbot.Domain.Documents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Documents;

public sealed class GeneratedDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TemplateId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFieldsAndExpiry()
    {
        var contactId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var doc = GeneratedDocument.Create(TenantId, TemplateId, "/files/q.pdf", Now, contactId, userId, "sha256abc");

        doc.TenantId.Should().Be(TenantId);
        doc.TemplateId.Should().Be(TemplateId);
        doc.FileUrl.Should().Be("/files/q.pdf");
        doc.ContactId.Should().Be(contactId);
        doc.GeneratedBy.Should().Be(userId);
        doc.FileHash.Should().Be("sha256abc");
        doc.CreatedAt.Should().Be(Now);
        doc.ExpiresAt.Should().Be(Now.AddDays(GeneratedDocument.LinkValidityDays));
        doc.SentVia.Should().BeNull();
        doc.SentAt.Should().BeNull();
        doc.OpenedAt.Should().BeNull();
    }

    [Fact]
    public void IsExpired_ReturnsFalseBeforeExpiry()
    {
        var doc = GeneratedDocument.Create(TenantId, TemplateId, "/f.pdf", Now);

        doc.IsExpired(Now.AddDays(6)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ReturnsTrueAfterExpiry()
    {
        var doc = GeneratedDocument.Create(TenantId, TemplateId, "/f.pdf", Now);

        doc.IsExpired(Now.AddDays(8)).Should().BeTrue();
    }

    [Fact]
    public void MarkSent_SetsSentViaAndTimestamp()
    {
        var doc = GeneratedDocument.Create(TenantId, TemplateId, "/f.pdf", Now);

        doc.MarkSent("email", Now.AddHours(1));

        doc.SentVia.Should().Be("email");
        doc.SentAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void MarkOpened_SetsOpenedAt()
    {
        var doc = GeneratedDocument.Create(TenantId, TemplateId, "/f.pdf", Now);

        doc.MarkOpened(Now.AddHours(2));

        doc.OpenedAt.Should().Be(Now.AddHours(2));
    }
}
