using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class InboxSearchServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 15, 45, 0, TimeSpan.Zero);

    [Fact]
    public async Task SearchAsync_matches_message_contact_and_thread_without_cross_tenant_leak()
    {
        using var fx = new TestApiAppDb(TenantId);
        var lan = Contact.Create(TenantId, "Nguyen Lan", Now.AddMinutes(-30));
        fx.Db.Entry(lan).Property(nameof(Contact.Email)).CurrentValue = "lan@example.com";
        var hskConversation = Conversation.Open(TenantId, "zalo", "thread-hsk-3", Now.AddMinutes(-20), lan.Id);
        hskConversation.AppendMessage("in", "customer", "Em can hoc phi HSK3", "text", Now.AddMinutes(-10));
        hskConversation.AppendMessage("out", "agent", "Hoc phi HSK3 la 3 trieu", "text", Now.AddMinutes(-5));

        var minh = Contact.Create(TenantId, "Tran Minh", Now.AddMinutes(-25));
        var saleConversation = Conversation.Open(TenantId, "facebook", "thread-sale", Now.AddMinutes(-24), minh.Id);
        saleConversation.AppendMessage("in", "customer", "Can tu van lop giao tiep", "text", Now.AddMinutes(-23));

        var otherTenant = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var otherContact = Contact.Create(otherTenant, "Other HSK", Now);
        var otherConversation = Conversation.Open(otherTenant, "zalo", "thread-other-hsk", Now, otherContact.Id);
        otherConversation.AppendMessage("in", "customer", "HSK3 secret", "text", Now);

        fx.Db.AddRange(lan, hskConversation, minh, saleConversation, otherContact, otherConversation);
        await fx.Db.SaveChangesAsync();
        var sut = new InboxSearchService(fx.Db);

        var messageResult = await sut.SearchAsync(TenantId, "hsk3", status: null, platform: null, page: 1, pageSize: 20, CancellationToken.None);
        var contactResult = await sut.SearchAsync(TenantId, "nguyen lan", status: null, platform: null, page: 1, pageSize: 20, CancellationToken.None);
        var threadResult = await sut.SearchAsync(TenantId, "thread-sale", status: null, platform: null, page: 1, pageSize: 20, CancellationToken.None);

        messageResult.Total.Should().Be(1);
        messageResult.Items.Should().ContainSingle(i => i.Id == hskConversation.Id);
        messageResult.Items[0].ContactDisplayName.Should().Be("Nguyen Lan");
        messageResult.Items[0].LastMessagePreview.Should().Be("Hoc phi HSK3 la 3 trieu");
        messageResult.Items.Should().NotContain(i => i.Id == otherConversation.Id);

        contactResult.Items.Should().ContainSingle(i => i.Id == hskConversation.Id);
        threadResult.Items.Should().ContainSingle(i => i.Id == saleConversation.Id);
    }
}
