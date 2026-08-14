using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Security.Claims;

namespace Clawbot.Api.Tests.Services;

/// <summary>
/// Trang "Khách hàng tiềm năng": role Sale chỉ thấy lead của kênh Pancake mình phụ trách,
/// Admin/SalesLead thấy toàn bộ lead của tenant.
/// </summary>
public sealed class LeadScopeResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetScopeAsync_RoleWithReadAllPermission_ReturnsUnrestrictedScope()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var user = fixture.PrincipalFor(RbacSeeder.SalesLead, Guid.NewGuid(), hasReadAll: true);

        var scope = await fixture.Resolver.GetScopeAsync(user, CancellationToken.None);

        scope.Unrestricted.Should().BeTrue();
    }

    [Fact]
    public async Task GetScopeAsync_SaleWithoutReadAll_ReturnsOwnInboxesOnly()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var tung = Guid.NewGuid();
        var tungInbox = await fixture.SeedPancakeChannelAsync("Kênh Tùng", tung);
        await fixture.SeedPancakeChannelAsync("Kênh Mai", Guid.NewGuid());
        var user = fixture.PrincipalFor(RbacSeeder.Sale, tung, hasReadAll: false);

        var scope = await fixture.Resolver.GetScopeAsync(user, CancellationToken.None);

        scope.Unrestricted.Should().BeFalse();
        scope.UserId.Should().Be(tung);
        scope.InboxIds.Should().BeEquivalentTo(new[] { tungInbox.Id });
    }

    [Fact]
    public async Task GetScopeAsync_SaleWithNoPancakeChannel_ReturnsEmptyInboxList()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var user = fixture.PrincipalFor(RbacSeeder.Sale, Guid.NewGuid(), hasReadAll: false);

        var scope = await fixture.Resolver.GetScopeAsync(user, CancellationToken.None);

        scope.Unrestricted.Should().BeFalse();
        scope.InboxIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetScopeAsync_ApiKeyWithLeadsReadAllScope_ReturnsUnrestrictedScope()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var identity = new ClaimsIdentity([new Claim("perm", LeadScopeResolver.ReadAllPermission)], "ApiKey");

        var scope = await fixture.Resolver.GetScopeAsync(new ClaimsPrincipal(identity), CancellationToken.None);

        scope.Unrestricted.Should().BeTrue();
    }

    [Fact]
    public async Task GetScopeAsync_NoRoleIdClaim_ReturnsRestrictedScope()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer");

        var scope = await fixture.Resolver.GetScopeAsync(new ClaimsPrincipal(identity), CancellationToken.None);

        scope.Unrestricted.Should().BeFalse();
        scope.InboxIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyLeadScope_UnrestrictedScope_KeepsEveryTenantLead()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var tungInbox = await fixture.SeedPancakeChannelAsync("Kênh Tùng", Guid.NewGuid());
        var maiInbox = await fixture.SeedPancakeChannelAsync("Kênh Mai", Guid.NewGuid());
        var tungLead = await fixture.SeedLeadInInboxAsync(tungInbox);
        var maiLead = await fixture.SeedLeadInInboxAsync(maiInbox);

        var ids = await fixture.Db.Leads
            .ApplyLeadScope(LeadScope.All, fixture.Db)
            .Select(l => l.Id)
            .ToListAsync();

        ids.Should().BeEquivalentTo(new[] { tungLead.Id, maiLead.Id });
    }

    [Fact]
    public async Task ApplyLeadScope_SaleScope_KeepsOnlyLeadsFromOwnPancakeChannel()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var tung = Guid.NewGuid();
        var tungInbox = await fixture.SeedPancakeChannelAsync("Kênh Tùng", tung);
        var maiInbox = await fixture.SeedPancakeChannelAsync("Kênh Mai", Guid.NewGuid());
        var tungLead = await fixture.SeedLeadInInboxAsync(tungInbox);
        await fixture.SeedLeadInInboxAsync(maiInbox);
        var user = fixture.PrincipalFor(RbacSeeder.Sale, tung, hasReadAll: false);
        var scope = await fixture.Resolver.GetScopeAsync(user, CancellationToken.None);

        var ids = await fixture.Db.Leads
            .ApplyLeadScope(scope, fixture.Db)
            .Select(l => l.Id)
            .ToListAsync();

        ids.Should().BeEquivalentTo(new[] { tungLead.Id });
    }

    [Fact]
    public async Task ApplyLeadScope_SaleScope_KeepsLeadAssignedToSaleFromAnotherChannel()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var tung = Guid.NewGuid();
        await fixture.SeedPancakeChannelAsync("Kênh Tùng", tung);
        var maiInbox = await fixture.SeedPancakeChannelAsync("Kênh Mai", Guid.NewGuid());
        // Lead vào kênh Mai nhưng được gán trực tiếp cho Tùng -> Tùng vẫn phải thấy.
        var handedOver = await fixture.SeedLeadInInboxAsync(maiInbox, ownerUserId: tung);
        var user = fixture.PrincipalFor(RbacSeeder.Sale, tung, hasReadAll: false);
        var scope = await fixture.Resolver.GetScopeAsync(user, CancellationToken.None);

        var ids = await fixture.Db.Leads
            .ApplyLeadScope(scope, fixture.Db)
            .Select(l => l.Id)
            .ToListAsync();

        ids.Should().BeEquivalentTo(new[] { handedOver.Id });
    }

    [Fact]
    public async Task ApplyLeadScope_SaleWithNoChannel_HidesEveryLead()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var maiInbox = await fixture.SeedPancakeChannelAsync("Kênh Mai", Guid.NewGuid());
        await fixture.SeedLeadInInboxAsync(maiInbox);
        var user = fixture.PrincipalFor(RbacSeeder.Sale, Guid.NewGuid(), hasReadAll: false);
        var scope = await fixture.Resolver.GetScopeAsync(user, CancellationToken.None);

        var ids = await fixture.Db.Leads
            .ApplyLeadScope(scope, fixture.Db)
            .Select(l => l.Id)
            .ToListAsync();

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportCsvAsync_SaleScope_ExcludesLeadsFromOtherChannels()
    {
        await using var fixture = await LeadScopeFixture.CreateAsync();
        var tung = Guid.NewGuid();
        var tungInbox = await fixture.SeedPancakeChannelAsync("Kênh Tùng", tung);
        var maiInbox = await fixture.SeedPancakeChannelAsync("Kênh Mai", Guid.NewGuid());
        var tungLead = await fixture.SeedLeadInInboxAsync(tungInbox);
        var maiLead = await fixture.SeedLeadInInboxAsync(maiInbox);
        var user = fixture.PrincipalFor(RbacSeeder.Sale, tung, hasReadAll: false);
        var scope = await fixture.Resolver.GetScopeAsync(user, CancellationToken.None);

        var export = await fixture.Csv.ExportCsvAsync(fixture.TenantId, scope);

        export.Content.Should().Contain(tungLead.Id.ToString());
        export.Content.Should().NotContain(maiLead.Id.ToString());
    }

    private sealed class LeadScopeFixture(
        SqliteConnection connection,
        AppDbContext db,
        Guid tenantId,
        IPermissionResolver permissions) : IAsyncDisposable
    {
        public Guid TenantId { get; } = tenantId;
        public AppDbContext Db { get; } = db;
        public LeadCsvService Csv { get; } = new(db, new FixedClock(Now));
        private IPermissionResolver Permissions { get; } = permissions;

        // Resolver cache scope theo instance (per-request) nên mỗi lần gọi tạo mới.
        public LeadScopeResolver Resolver => new(Db, Permissions);

        public static async Task<LeadScopeFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            // Phải có tenant thật: query filter so TenantId với tenant hiện tại, accessor rỗng
            // sẽ lọc sạch mọi dòng và làm test xanh/đỏ sai lý do.
            var tenantId = Guid.NewGuid();
            var db = new AppDbContext(options, new StubTenantAccessor(tenantId));
            await db.Database.EnsureCreatedAsync();
            return new LeadScopeFixture(connection, db, tenantId, Substitute.For<IPermissionResolver>());
        }

        /// <summary>Kênh Pancake của một sale = 1 inbox + 1 dòng inbox_members.</summary>
        public async Task<Inbox> SeedPancakeChannelAsync(string name, Guid agentId)
        {
            var inbox = Inbox.Create(TenantId, name, "facebook", $"page-{Guid.NewGuid():N}");
            Db.Inboxes.Add(inbox);
            Db.InboxMembers.Add(InboxMember.Create(TenantId, inbox.Id, agentId));
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return inbox;
        }

        public async Task<Lead> SeedLeadInInboxAsync(Inbox inbox, Guid? ownerUserId = null)
        {
            var contact = Contact.Create(TenantId, $"Khách {inbox.Name}", Now.AddDays(-3));
            Db.Contacts.Add(contact);
            Db.Conversations.Add(Conversation.Open(
                TenantId, "facebook", $"thread-{Guid.NewGuid():N}", Now.AddDays(-2),
                contactId: contact.Id, inboxId: inbox.Id));

            var lead = Lead.Create(TenantId, contact.Id, "facebook", Now.AddDays(-2));
            if (ownerUserId is { } owner) lead.Assign(owner);
            Db.Leads.Add(lead);

            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return lead;
        }

        public ClaimsPrincipal PrincipalFor(string roleName, Guid userId, bool hasReadAll)
        {
            var roleId = RbacSeeder.RoleIds[roleName];
            Permissions.GetPermissionsAsync(roleId, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlySet<string>>(hasReadAll
                    ? new HashSet<string>(StringComparer.Ordinal) { "leads:read", LeadScopeResolver.ReadAllPermission }
                    : new HashSet<string>(StringComparer.Ordinal) { "leads:read" }));

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim("role_id", roleId.ToString()),
                    new Claim("tenant_id", TenantId.ToString()),
                ],
                "Bearer");
            return new ClaimsPrincipal(identity);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : Clawbot.SharedKernel.Time.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class StubTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
