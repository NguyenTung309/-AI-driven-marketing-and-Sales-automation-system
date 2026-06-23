using System.Security.Claims;
using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests;

public sealed class UserInboxResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AdminRoleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SaleRoleId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SaleUserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task GetInboxIdsAsync_admin_with_admin_inboxes_permission_returns_empty_list()
    {
        using var fx = new TestApiAppDb(TenantId);
        var permResolver = Substitute.For<IPermissionResolver>();
        permResolver.GetPermissionsAsync(AdminRoleId, default)
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "admin:inboxes", "conversations:read" }));

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("role_id", AdminRoleId.ToString()),
        }));

        var sut = new UserInboxResolver(fx.Db, permResolver);
        var result = await sut.GetInboxIdsAsync(user, default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInboxIdsAsync_sale_without_any_InboxMembers_returns_empty_guid_sentinel()
    {
        using var fx = new TestApiAppDb(TenantId);
        var permResolver = Substitute.For<IPermissionResolver>();
        permResolver.GetPermissionsAsync(SaleRoleId, default)
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "conversations:read" }));

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, SaleUserId.ToString()),
            new Claim("role_id", SaleRoleId.ToString()),
        }));

        var sut = new UserInboxResolver(fx.Db, permResolver);
        var result = await sut.GetInboxIdsAsync(user, default);

        result.Should().ContainSingle();
        result[0].Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task GetInboxIdsAsync_sale_with_InboxMembers_returns_their_inbox_ids()
    {
        using var fx = new TestApiAppDb(TenantId);
        var permResolver = Substitute.For<IPermissionResolver>();
        permResolver.GetPermissionsAsync(SaleRoleId, default)
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "conversations:read" }));

        var inbox = Inbox.Create(TenantId, "FB Page", "facebook", "page-123");
        fx.Db.Inboxes.Add(inbox);
        fx.Db.InboxMembers.Add(InboxMember.Create(inbox.Id, SaleUserId));
        await fx.Db.SaveChangesAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, SaleUserId.ToString()),
            new Claim("role_id", SaleRoleId.ToString()),
        }));

        var sut = new UserInboxResolver(fx.Db, permResolver);
        var result = await sut.GetInboxIdsAsync(user, default);

        result.Should().ContainSingle();
        result[0].Should().Be(inbox.Id);
    }

    [Fact]
    public async Task GetInboxIdsAsync_unparseable_userId_returns_empty_guid_sentinel()
    {
        using var fx = new TestApiAppDb(TenantId);
        var permResolver = Substitute.For<IPermissionResolver>();
        permResolver.GetPermissionsAsync(SaleRoleId, default)
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "conversations:read" }));

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "not-a-guid"),
            new Claim("role_id", SaleRoleId.ToString()),
        }));

        var sut = new UserInboxResolver(fx.Db, permResolver);
        var result = await sut.GetInboxIdsAsync(user, default);

        result.Should().ContainSingle();
        result[0].Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task GetInboxIdsAsync_uses_cache_on_second_call()
    {
        using var fx = new TestApiAppDb(TenantId);
        var permResolver = Substitute.For<IPermissionResolver>();
        permResolver.GetPermissionsAsync(SaleRoleId, default)
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "conversations:read" }));

        var inbox = Inbox.Create(TenantId, "Zalo OA", "zalo", "oa-456");
        fx.Db.Inboxes.Add(inbox);
        fx.Db.InboxMembers.Add(InboxMember.Create(inbox.Id, SaleUserId));
        await fx.Db.SaveChangesAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, SaleUserId.ToString()),
            new Claim("role_id", SaleRoleId.ToString()),
        }));

        var sut = new UserInboxResolver(fx.Db, permResolver);
        var first = await sut.GetInboxIdsAsync(user, default);
        fx.Db.InboxMembers.RemoveRange(fx.Db.InboxMembers);
        await fx.Db.SaveChangesAsync();
        var second = await sut.GetInboxIdsAsync(user, default);

        second.Should().BeEquivalentTo(first);
    }
}