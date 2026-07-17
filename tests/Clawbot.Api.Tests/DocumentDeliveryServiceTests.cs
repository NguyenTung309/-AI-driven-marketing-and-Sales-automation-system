using Clawbot.Api.Services;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class DocumentDeliveryServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TrySendAsync_dispatches_zalo_document_to_latest_zalo_thread_and_marks_sent()
    {
        using var fx = new TestApiAppDb(TenantId);
        var contact = Contact.Create(TenantId, "Nguyen Lan", Now);
        var template = DocumentTemplate.Create(TenantId, "quote", "quote", "<p>{{customer_name}}</p>", Now);
        var doc = GeneratedDocument.Create(TenantId, template.Id, "https://files.example/quote.pdf", Now, contact.Id);
        var oldConversation = Conversation.Open(TenantId, "zalo", "page-1:old-thread", Now.AddMinutes(-10), contact.Id);
        oldConversation.AppendMessage("in", "contact", "old", "text", Now.AddMinutes(-10));
        var latestConversation = Conversation.Open(TenantId, "zalo", "page-1:new-thread", Now, contact.Id);
        latestConversation.AppendMessage("in", "contact", "new", "text", Now);
        fx.Db.AddRange(contact, template, doc, oldConversation, latestConversation);
        await fx.Db.SaveChangesAsync();

        var adapter = new CapturingChannelAdapter();
        var sut = new DocumentDeliveryService(
            fx.Db,
            new NoopEmailSender(),
            [adapter],
            new FixedClock(Now.AddMinutes(5)));

        var sent = await sut.TrySendAsync(TenantId, doc.Id, "zalo");

        sent.Should().BeTrue();
        adapter.Sends.Should().ContainSingle().Which.Should().Be((
            TenantId,
            "page-1:new-thread",
            $"Xin chào, tài liệu của bạn đã sẵn sàng: {doc.FileUrl}\nLiên kết có hiệu lực đến 22/06/2026."));
        fx.Db.ChangeTracker.Clear();
        var saved = await fx.Db.GeneratedDocuments.FindAsync(doc.Id);
        saved!.SentVia.Should().Be("zalo");
        saved.SentAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public async Task TrySendAsync_dispatches_email_document_to_contact_email_and_marks_sent()
    {
        using var fx = new TestApiAppDb(TenantId);
        var contact = Contact.Create(TenantId, "Nguyen Lan", Now);
        var template = DocumentTemplate.Create(TenantId, "quote", "quote", "<p>{{customer_name}}</p>", Now);
        var doc = GeneratedDocument.Create(TenantId, template.Id, "https://files.example/quote.pdf", Now, contact.Id);
        fx.Db.AddRange(contact, template, doc);
        fx.Db.Entry(contact).Property("Email").CurrentValue = "lan@example.com";
        await fx.Db.SaveChangesAsync();

        var email = new CapturingEmailSender();
        var sut = new DocumentDeliveryService(
            fx.Db,
            email,
            [],
            new FixedClock(Now.AddMinutes(3)));

        var sent = await sut.TrySendAsync(TenantId, doc.Id, "email");

        sent.Should().BeTrue();
        email.Sends.Should().ContainSingle().Which.Should().Be((
            "lan@example.com",
            "Tài liệu từ Học Bá",
            $"Xin chào, tài liệu của bạn đã sẵn sàng: {doc.FileUrl}\nLiên kết có hiệu lực đến 22/06/2026."));
        fx.Db.ChangeTracker.Clear();
        var saved = await fx.Db.GeneratedDocuments.FindAsync(doc.Id);
        saved!.SentVia.Should().Be("email");
        saved.SentAt.Should().Be(Now.AddMinutes(3));
    }

    private sealed class CapturingChannelAdapter : IChannelAdapter
    {
        public string Name => "pancake";
        public List<(Guid TenantId, string ExternalThreadId, string Text)> Sends { get; } = [];

        public Task<bool> VerifyWebhookSignatureAsync(Guid tenantId, string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChannelMessage>>([]);

        public Task<string?> SendAsync(Guid tenantId, string externalThreadId, string text, CancellationToken ct = default)
        {
            Sends.Add((tenantId, externalThreadId, text));
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string Recipient, string Subject, string Body)> Sends { get; } = [];

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
        {
            Sends.Add((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
