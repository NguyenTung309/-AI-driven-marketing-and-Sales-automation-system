using Clawbot.Agents.Core.Docs;
using Clawbot.Api.Services;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests;

public sealed class DocumentDeliveryServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46];

    [Fact]
    public async Task TrySendAsync_UsesDirectRecipientBeforeContactEmail()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var contact = Contact.Create(tenantId, "Khách hàng", Now);
        db.Contacts.Add(contact);
        db.Entry(contact).Property(nameof(Contact.Email)).CurrentValue = "contact@example.com";
        var document = GeneratedDocument.Create(
            tenantId,
            Guid.NewGuid(),
            "/generated-docs/quote.pdf",
            Now,
            contact.Id);
        db.GeneratedDocuments.Add(document);
        await db.SaveChangesAsync();

        var email = Substitute.For<IEmailSender>();
        var service = CreateService(db, email);

        // Act
        var sent = await service.TrySendAsync(
            tenantId,
            document.Id,
            "email",
            "direct@example.com",
            CancellationToken.None);

        // Assert
        sent.Should().BeTrue();
        await email.Received(1).SendAsync(
            "direct@example.com",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<EmailAttachment>>(a => a.Count == 1),
            Arg.Any<CancellationToken>());
        document.SentVia.Should().Be("email");
        document.SentAt.Should().Be(Now);
    }

    [Fact]
    public async Task TrySendAsync_FallsBackToContactEmail()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var contact = Contact.Create(tenantId, "Khách hàng", Now);
        db.Contacts.Add(contact);
        db.Entry(contact).Property(nameof(Contact.Email)).CurrentValue = "contact@example.com";
        var document = GeneratedDocument.Create(
            tenantId,
            Guid.NewGuid(),
            "/generated-docs/quote.pdf",
            Now,
            contact.Id);
        db.GeneratedDocuments.Add(document);
        await db.SaveChangesAsync();

        var email = Substitute.For<IEmailSender>();
        var service = CreateService(db, email);

        // Act
        var sent = await service.TrySendAsync(
            tenantId,
            document.Id,
            "email",
            recipientEmail: null,
            CancellationToken.None);

        // Assert
        sent.Should().BeTrue();
        await email.Received(1).SendAsync(
            "contact@example.com",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<EmailAttachment>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendAsync_RejectsInvalidDirectRecipientWithoutFallingBack()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var contact = Contact.Create(tenantId, "Khách hàng", Now);
        db.Contacts.Add(contact);
        db.Entry(contact).Property(nameof(Contact.Email)).CurrentValue = "contact@example.com";
        var document = GeneratedDocument.Create(
            tenantId,
            Guid.NewGuid(),
            "/generated-docs/quote.pdf",
            Now,
            contact.Id);
        db.GeneratedDocuments.Add(document);
        await db.SaveChangesAsync();

        var email = Substitute.For<IEmailSender>();
        var service = CreateService(db, email);

        // Act
        var sent = await service.TrySendAsync(
            tenantId,
            document.Id,
            "email",
            "bad-address",
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        email.ReceivedCalls().Should().BeEmpty();
        document.SentAt.Should().BeNull();
    }

    [Fact]
    public async Task TrySendAsync_DoesNotMarkDocumentSentWhenSenderFails()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var document = GeneratedDocument.Create(
            tenantId,
            Guid.NewGuid(),
            "/generated-docs/quote.pdf",
            Now);
        db.GeneratedDocuments.Add(document);
        await db.SaveChangesAsync();

        var email = Substitute.For<IEmailSender>();
        email.SendAsync(
                "direct@example.com",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EmailAttachment>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("smtp_failed")));
        var service = CreateService(db, email);

        // Act
        Func<Task> act = () => service.TrySendAsync(
            tenantId,
            document.Id,
            "email",
            "direct@example.com",
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("smtp_failed");
        document.SentAt.Should().BeNull();
    }

    private static DocumentDeliveryService CreateService(
        AppDbContext db,
        IEmailSender email,
        IDocumentStorage? storage = null) =>
        new(db,
            email,
            Array.Empty<IChannelAdapter>(),
            storage ?? CreateStorage(),
            new DocsStorageOptions(),
            new FixedClock(Now));

    // FileUrl trong test là key nội bộ "/generated-docs/..." nên service đi nhánh đính kèm — storage
    // phải trả được bytes, nếu không mọi test email sẽ rơi vào nhánh fallback link.
    private static IDocumentStorage CreateStorage()
    {
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PdfBytes);
        return storage;
    }

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FixedTenantAccessor(tenantId));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
