using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class AdminInboxEndpointsTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task CreateInbox_persists_inbox_with_token_and_member()
    {
        using var fx = new TestApiAppDb(TenantId);

        var inbox = Inbox.Create(TenantId, "FB Page", "facebook", "page123");
        inbox.SetAccessToken("token-abc", DateTimeOffset.UtcNow);
        fx.Db.Inboxes.Add(inbox);
        fx.Db.InboxMembers.Add(InboxMember.Create(TenantId, inbox.Id, Guid.NewGuid()));
        await fx.Db.SaveChangesAsync();

        var saved = await fx.Db.Inboxes.FirstAsync(i => i.Id == inbox.Id);
        saved.Name.Should().Be("FB Page");
        saved.Platform.Should().Be("facebook");
        saved.EncryptedAccessToken.Should().Be("token-abc");

        var member = await fx.Db.InboxMembers.FirstAsync(m => m.InboxId == inbox.Id);
        member.AgentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListInboxes_includes_HasToken()
    {
        using var fx = new TestApiAppDb(TenantId);

        var withToken = Inbox.Create(TenantId, "With Token", "facebook", "fb1");
        withToken.SetAccessToken("tok", DateTimeOffset.UtcNow);
        fx.Db.Inboxes.Add(withToken);

        var withoutToken = Inbox.Create(TenantId, "No Token", "zalo", "za1");
        fx.Db.Inboxes.Add(withoutToken);
        await fx.Db.SaveChangesAsync();

        var list = await fx.Db.Inboxes
            .Where(i => i.TenantId == TenantId && i.DeletedAt == null)
            .Select(i => new { i.Id, i.Name, HasToken = i.EncryptedAccessToken != null })
            .OrderBy(i => i.Name)
            .ToListAsync();

        list.Should().Contain(i => i.Name == "With Token" && i.HasToken);
        list.Should().Contain(i => i.Name == "No Token" && !i.HasToken);
    }
}
