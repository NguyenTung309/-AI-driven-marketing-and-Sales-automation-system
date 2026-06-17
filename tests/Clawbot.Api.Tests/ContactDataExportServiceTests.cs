using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Domain.Leads;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ContactDataExportServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("babababa-baba-baba-baba-babababababa");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportAsync_returns_contact_scoped_personal_data_without_cross_tenant_rows()
    {
        using var fx = new TestApiAppDb(TenantId);
        var contact = Contact.Create(TenantId, "Nguyen Lan", Now.AddDays(-5));
        fx.Db.Entry(contact).Property(nameof(Contact.Phone)).CurrentValue = "0912345678";
        fx.Db.Entry(contact).Property(nameof(Contact.Email)).CurrentValue = "lan@example.com";
        var externalId = ContactExternalId.Create(contact.Id, "zalo", "zalo-user-1", Now.AddDays(-4));
        var conversation = Conversation.Open(TenantId, "zalo", "thread-1", Now.AddDays(-3), contact.Id);
        conversation.AppendMessage("in", "contact", "toi muon hoc HSK4", "text", Now.AddDays(-3), externalMessageId: "msg-1");
        conversation.AppendMessage("out", "agent", "em gui hoc phi", "text", Now.AddDays(-3).AddMinutes(1), externalMessageId: "msg-2");
        var lead = Lead.Create(TenantId, contact.Id, "zalo", Now.AddDays(-2));
        lead.AdjustScore(72, "asked pricing", Now.AddDays(-2).AddMinutes(5));
        var template = DocumentTemplate.Create(TenantId, "quote", "quote", "<p>Quote</p>", Now.AddDays(-1));
        var document = GeneratedDocument.Create(TenantId, template.Id, "https://files.example/quote.pdf", Now.AddDays(-1), contact.Id, fileHash: "hash-1");
        var otherTenantContact = Contact.Create(Guid.NewGuid(), "Other Tenant", Now);
        var otherTenantConversation = Conversation.Open(otherTenantContact.TenantId, "zalo", "thread-other", Now, otherTenantContact.Id);
        fx.Db.AddRange(contact, externalId, conversation, lead, template, document, otherTenantContact, otherTenantConversation);
        await fx.Db.SaveChangesAsync();
        var sut = new ContactDataExportService(fx.Db);

        var result = await sut.ExportAsync(TenantId, contact.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FileName.Should().Be($"contact-{contact.Id:N}-data-export.json");
        result.Export.Contact.Id.Should().Be(contact.Id);
        result.Export.Contact.DisplayName.Should().Be("Nguyen Lan");
        result.Export.Contact.Phone.Should().Be("0912345678");
        result.Export.ExternalIds.Should().ContainSingle(e => e.Platform == "zalo" && e.ExternalId == "zalo-user-1");
        result.Export.Conversations.Should().ContainSingle().Which.Messages.Should().HaveCount(2);
        result.Export.Leads.Should().ContainSingle(l => l.Id == lead.Id && l.Score == 72 && l.Stage == "hot");
        result.Export.LeadActivities.Should().ContainSingle(a => a.LeadId == lead.Id && a.ActivityType == "score_adjust");
        result.Export.Documents.Should().ContainSingle(d => d.Id == document.Id && d.FileHash == "hash-1");
        result.Export.Conversations.Should().NotContain(c => c.ExternalThreadId == "thread-other");
    }

    [Fact]
    public async Task ExportAsync_returns_null_for_contact_outside_tenant()
    {
        using var fx = new TestApiAppDb(TenantId);
        var otherTenantContact = Contact.Create(Guid.NewGuid(), "Other Tenant", Now);
        fx.Db.Contacts.Add(otherTenantContact);
        await fx.Db.SaveChangesAsync();
        var sut = new ContactDataExportService(fx.Db);

        var result = await sut.ExportAsync(TenantId, otherTenantContact.Id, CancellationToken.None);

        result.Should().BeNull();
    }
}
