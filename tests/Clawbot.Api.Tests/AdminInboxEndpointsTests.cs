using Clawbot.Domain.Channels;
using Clawbot.Domain.Users;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class AdminInboxEndpointsTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static AppDbContext CreateDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn);
        var db = new AppDbContext(builder.Options, new FixedTenantAccessor(TenantId));
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task CreateInbox_persists_inbox_with_token_and_member()
    {
        using var db = CreateDb();
        var tenant = new Tenant { Id = TenantId, Name = "Test", Slug = "test" };
        db.Tenants.Add(tenant);

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            DisplayName = "Sale A",
            Email = "sale@test.com",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Simulate AdminInboxEndpoints.CreateInboxAsync logic
        var inbox = Inbox.Create(TenantId, "FB Page", "facebook", "page123");
        inbox.SetAccessToken("token-abc", DateTimeOffset.UtcNow);
        db.Inboxes.Add(inbox);
        db.InboxMembers.Add(InboxMember.Create(inbox.Id, user.Id));
        await db.SaveChangesAsync();

        var saved = await db.Inboxes.FirstAsync(i => i.Id == inbox.Id);
        saved.Name.Should().Be("FB Page");
        saved.Platform.Should().Be("facebook");
        saved.ExternalPageId.Should().Be("page123");
        saved.EncryptedAccessToken.Should().Be("token-abc");

        var member = await db.InboxMembers.FirstAsync(m => m.InboxId == inbox.Id);
        member.AgentId.Should().Be(user.Id);
    }

    [Fact]
    public async Task ListInboxes_includes_HasToken()
    {
        using var db = CreateDb();
        var tenant = new Tenant { Id = TenantId, Name = "Test", Slug = "test" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var inboxWithToken = Inbox.Create(TenantId, "With Token", "facebook", "fb1");
        inboxWithToken.SetAccessToken("tok", DateTimeOffset.UtcNow);
        db.Inboxes.Add(inboxWithToken);

        var inboxWithoutToken = Inbox.Create(TenantId, "No Token", "zalo", "za1");
        db.Inboxes.Add(inboxWithoutToken);
        await db.SaveChangesAsync();

        var query = db.Inboxes
            .Where(i => i.TenantId == TenantId && i.DeletedAt == null)
            .Select(i => new
            {
                i.Id,
                i.Name,
                HasToken = i.EncryptedAccessToken != null,
            })
            .OrderBy(i => i.Name);

        var list = await query.ToListAsync();

        list.Should().Contain(i => i.Name == "With Token" && i.HasToken);
        list.Should().Contain(i => i.Name == "No Token" && !i.HasToken);
    }
}

internal sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
{
    public TenantAccessorResult Require() => new() { TenantId = tenantId };
    public TenantAccessorResult? TryGet() => new() { TenantId = tenantId };
}
