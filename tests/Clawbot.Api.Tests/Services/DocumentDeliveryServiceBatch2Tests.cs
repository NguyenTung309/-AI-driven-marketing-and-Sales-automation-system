using Clawbot.Agents.Core.Docs;
using Clawbot.Api.Services;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Services;

// Batch 2: phủ các nhánh còn lại của DocumentDeliveryService mà file DocumentDeliveryServiceTests.cs
// ở gốc project chưa đụng (sentVia rỗng/không hỗ trợ, document không tồn tại, FileUrl tuyệt đối,
// contact không có email, storage thiếu file, toàn bộ nhánh zalo). Case "recipientEmail null +
// contact có email hợp lệ -> gửi tới email contact" đã được phủ bởi test gốc nên không lặp lại.
public sealed class DocumentDeliveryServiceBatch2Tests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46];

    [Fact]
    public async Task TrySendAsync_NullSentVia_ReturnsFalseWithoutTouchingDependencies()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var document = await fixture.SeedDocumentAsync("https://cdn.example.com/quote.pdf");
        var email = Substitute.For<IEmailSender>();
        var storage = CreateStorageReturning(PdfBytes);
        var adapter = CreatePancakeAdapter();
        var service = CreateService(fixture.Db, email, [adapter], storage);

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            null,
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        await email.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default(IReadOnlyList<EmailAttachment>)!, default);
        await storage.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
        await adapter.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task TrySendAsync_UnsupportedSentVia_ReturnsFalseWithoutTouchingDependencies()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var document = await fixture.SeedDocumentAsync("https://cdn.example.com/quote.pdf");
        var email = Substitute.For<IEmailSender>();
        var storage = CreateStorageReturning(PdfBytes);
        var adapter = CreatePancakeAdapter();
        var service = CreateService(fixture.Db, email, [adapter], storage);

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "sms",
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        await email.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default(IReadOnlyList<EmailAttachment>)!, default);
        await storage.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
        await adapter.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task TrySendAsync_Email_UnknownDocument_ReturnsFalseWithoutSending()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var email = Substitute.For<IEmailSender>();
        var service = CreateService(
            fixture.Db,
            email,
            Array.Empty<IChannelAdapter>(),
            CreateStorageReturning(PdfBytes));

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            Guid.NewGuid(),
            "email",
            "direct@example.com",
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        await email.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default(IReadOnlyList<EmailAttachment>)!, default);
    }

    [Fact]
    public async Task TrySendAsync_Email_AbsoluteFileUrl_SendsLinkBodyAndMarksSent()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var document = await fixture.SeedDocumentAsync("https://cdn.example.com/quote.pdf");
        var email = Substitute.For<IEmailSender>();
        var storage = CreateStorageReturning(PdfBytes);
        var service = CreateService(
            fixture.Db,
            email,
            Array.Empty<IChannelAdapter>(),
            storage);

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "email",
            "direct@example.com",
            CancellationToken.None);

        // Assert
        sent.Should().BeTrue();
        await email.Received(1).SendAsync(
            "direct@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("https://cdn.example.com/quote.pdf")),
            Arg.Is<IReadOnlyList<EmailAttachment>>(attachments => attachments.Count == 0),
            Arg.Any<CancellationToken>());
        // URL tuyệt đối thì không cần đọc file từ storage.
        await storage.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);

        fixture.Db.ChangeTracker.Clear();
        var reloaded = await fixture.Db.GeneratedDocuments.IgnoreQueryFilters()
            .SingleAsync(d => d.Id == document.Id && d.TenantId == fixture.TenantId);
        reloaded.SentVia.Should().Be("email");
        reloaded.SentAt.Should().Be(Now);
    }

    [Fact]
    public async Task TrySendAsync_Email_NoRecipient_ContactWithoutEmail_ReturnsFalse()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var contact = await fixture.SeedContactAsync(email: null);
        var document = await fixture.SeedDocumentAsync("/generated-docs/quote.pdf", contact.Id);
        var email = Substitute.For<IEmailSender>();
        var service = CreateService(
            fixture.Db,
            email,
            Array.Empty<IChannelAdapter>(),
            CreateStorageReturning(PdfBytes));

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "email",
            recipientEmail: null,
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        await email.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default(IReadOnlyList<EmailAttachment>)!, default);
    }

    [Fact]
    public async Task TrySendAsync_Email_InternalFileUrl_AttachesFileFromStorage()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var document = await fixture.SeedDocumentAsync("/generated-docs/quote.pdf");
        var email = Substitute.For<IEmailSender>();
        var storage = CreateStorageReturning(PdfBytes);
        var service = CreateService(
            fixture.Db,
            email,
            Array.Empty<IChannelAdapter>(),
            storage);

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "email",
            "direct@example.com",
            CancellationToken.None);

        // Assert
        sent.Should().BeTrue();
        // Storage key phải được cắt prefix PublicBaseUrl ("/generated-docs/").
        await storage.Received(1).ReadAsync("quote.pdf", Arg.Any<CancellationToken>());
        await email.Received(1).SendAsync(
            "direct@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => !body.Contains("/generated-docs/quote.pdf")),
            Arg.Is<IReadOnlyList<EmailAttachment>>(attachments =>
                attachments.Count == 1 &&
                attachments[0].FileName == "quote.pdf" &&
                attachments[0].ContentType == "application/pdf"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendAsync_Email_StorageFileMissing_FallsBackToLinkBody()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var document = await fixture.SeedDocumentAsync("/generated-docs/quote.pdf");
        var email = Substitute.For<IEmailSender>();
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<byte[]>(new FileNotFoundException("object missing")));
        var service = CreateService(
            fixture.Db,
            email,
            Array.Empty<IChannelAdapter>(),
            storage);

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "email",
            "direct@example.com",
            CancellationToken.None);

        // Assert — storage mất file vẫn gửi được mail với body link cũ, không đính kèm.
        sent.Should().BeTrue();
        await email.Received(1).SendAsync(
            "direct@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("/generated-docs/quote.pdf")),
            Arg.Is<IReadOnlyList<EmailAttachment>>(attachments => attachments.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrySendAsync_Zalo_DocumentWithoutContact_ReturnsFalse()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var document = await fixture.SeedDocumentAsync("/generated-docs/quote.pdf");
        var adapter = CreatePancakeAdapter();
        var service = CreateService(
            fixture.Db,
            Substitute.For<IEmailSender>(),
            [adapter],
            CreateStorageReturning(PdfBytes));

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "zalo",
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        await adapter.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task TrySendAsync_Zalo_NoZaloConversation_ReturnsFalse()
    {
        // Arrange — có contact nhưng hội thoại duy nhất là facebook, không phải zalo.
        await using var fixture = await DeliveryFixture.CreateAsync();
        var contact = await fixture.SeedContactAsync("contact@example.com");
        var document = await fixture.SeedDocumentAsync("/generated-docs/quote.pdf", contact.Id);
        await fixture.SeedConversationAsync(contact.Id, "facebook", "thread-fb-1");
        var adapter = CreatePancakeAdapter();
        var service = CreateService(
            fixture.Db,
            Substitute.For<IEmailSender>(),
            [adapter],
            CreateStorageReturning(PdfBytes));

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "zalo",
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        await adapter.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task TrySendAsync_Zalo_ValidThread_SendsViaPancakeAdapterAndMarksSent()
    {
        // Arrange
        await using var fixture = await DeliveryFixture.CreateAsync();
        var contact = await fixture.SeedContactAsync(email: null);
        var document = await fixture.SeedDocumentAsync("/generated-docs/quote.pdf", contact.Id);
        await fixture.SeedConversationAsync(contact.Id, "zalo", "zalo-thread-1");
        var email = Substitute.For<IEmailSender>();
        var adapter = CreatePancakeAdapter();
        var service = CreateService(
            fixture.Db,
            email,
            [adapter],
            CreateStorageReturning(PdfBytes));

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "zalo",
            CancellationToken.None);

        // Assert
        sent.Should().BeTrue();
        await adapter.Received(1).SendAsync(
            fixture.TenantId,
            "zalo",
            "zalo-thread-1",
            Arg.Is<string>(text => text.Contains("/generated-docs/quote.pdf")),
            Arg.Any<CancellationToken>());
        await email.DidNotReceiveWithAnyArgs().SendAsync(
            default!, default!, default!, default(IReadOnlyList<EmailAttachment>)!, default);

        fixture.Db.ChangeTracker.Clear();
        var reloaded = await fixture.Db.GeneratedDocuments.IgnoreQueryFilters()
            .SingleAsync(d => d.Id == document.Id && d.TenantId == fixture.TenantId);
        reloaded.SentVia.Should().Be("zalo");
        reloaded.SentAt.Should().Be(Now);
    }

    [Fact]
    public async Task TrySendAsync_Zalo_NoChannelAdapters_ReturnsFalse()
    {
        // Arrange — có conversation zalo hợp lệ nhưng không adapter nào đăng ký.
        await using var fixture = await DeliveryFixture.CreateAsync();
        var contact = await fixture.SeedContactAsync(email: null);
        var document = await fixture.SeedDocumentAsync("/generated-docs/quote.pdf", contact.Id);
        await fixture.SeedConversationAsync(contact.Id, "zalo", "zalo-thread-1");
        var service = CreateService(
            fixture.Db,
            Substitute.For<IEmailSender>(),
            Array.Empty<IChannelAdapter>(),
            CreateStorageReturning(PdfBytes));

        // Act
        var sent = await service.TrySendAsync(
            fixture.TenantId,
            document.Id,
            "zalo",
            CancellationToken.None);

        // Assert
        sent.Should().BeFalse();
        fixture.Db.ChangeTracker.Clear();
        var reloaded = await fixture.Db.GeneratedDocuments.IgnoreQueryFilters()
            .SingleAsync(d => d.Id == document.Id && d.TenantId == fixture.TenantId);
        reloaded.SentAt.Should().BeNull();
    }

    private static DocumentDeliveryService CreateService(
        AppDbContext db,
        IEmailSender email,
        IEnumerable<IChannelAdapter> channels,
        IDocumentStorage storage) =>
        new(db, email, channels, storage, new DocsStorageOptions(), CreateClock());

    private static IClock CreateClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private static IDocumentStorage CreateStorageReturning(byte[] bytes)
    {
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(bytes);
        return storage;
    }

    private static IChannelAdapter CreatePancakeAdapter()
    {
        var adapter = Substitute.For<IChannelAdapter>();
        adapter.Name.Returns("pancake");
        adapter.SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("ext-msg-id"));
        return adapter;
    }

    private sealed class DeliveryFixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; } = db;

        public static async Task<DeliveryFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            return new DeliveryFixture(connection, db);
        }

        public async Task<GeneratedDocument> SeedDocumentAsync(string fileUrl, Guid? contactId = null)
        {
            var document = GeneratedDocument.Create(
                TenantId,
                Guid.NewGuid(),
                fileUrl,
                Now,
                contactId: contactId);
            Db.GeneratedDocuments.Add(document);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return document;
        }

        public async Task<Contact> SeedContactAsync(string? email)
        {
            var contact = Contact.Create(TenantId, "Khách hàng", Now);
            Db.Contacts.Add(contact);
            if (email is not null)
            {
                // Contact.Email có private setter — ghi qua EF property entry như file test gốc.
                Db.Entry(contact).Property(nameof(Contact.Email)).CurrentValue = email;
            }
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return contact;
        }

        public async Task<Conversation> SeedConversationAsync(
            Guid contactId,
            string platform,
            string externalThreadId)
        {
            var conversation = Conversation.Open(
                TenantId,
                platform,
                externalThreadId,
                Now.AddHours(-1),
                contactId: contactId);
            Db.Conversations.Add(conversation);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return conversation;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
