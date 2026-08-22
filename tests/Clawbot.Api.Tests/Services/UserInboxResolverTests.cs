using System.Security.Claims;
using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Services;

/// <summary>
/// UserInboxResolver.GetInboxIdsAsync: role có "admin:inboxes" -> [] (không filter);
/// role không có quyền đó thì trả đúng InboxMember của user; không có dòng nào hoặc claim
/// hỏng thì trả sentinel [Guid.Empty] (lọc rỗng, không phải "không lọc").
/// </summary>
public sealed class UserInboxResolverTests : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly IPermissionResolver _permissions;
    private readonly Guid _tenantId = Guid.NewGuid();

    public UserInboxResolverTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"user-inbox-resolver-{Guid.NewGuid():N}")
            .Options;
        _db = new AppDbContext(options, new StubTenantAccessor(_tenantId));
        _permissions = Substitute.For<IPermissionResolver>();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private UserInboxResolver CreateResolver() => new(_db, _permissions);

    private static ClaimsPrincipal PrincipalFor(Guid userId, Guid roleId)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("role_id", roleId.ToString()),
            ],
            "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private async Task<Inbox> SeedInboxWithMemberAsync(Guid agentId)
    {
        var inbox = Inbox.Create(_tenantId, "Kênh test", "facebook", $"page-{Guid.NewGuid():N}");
        _db.Inboxes.Add(inbox);
        _db.InboxMembers.Add(InboxMember.Create(_tenantId, inbox.Id, agentId));
        await _db.SaveChangesAsync();
        return inbox;
    }

    [Fact]
    public async Task GetInboxIdsAsync_RoleHasAdminInboxesPermission_ReturnsEmptyList()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _permissions.GetPermissionsAsync(roleId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(StringComparer.Ordinal) { "admin:inboxes" }));
        var resolver = CreateResolver();

        var ids = await resolver.GetInboxIdsAsync(PrincipalFor(userId, roleId), CancellationToken.None);

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInboxIdsAsync_RoleWithoutAdminPermission_ReturnsOwnInboxMembers()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _permissions.GetPermissionsAsync(roleId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(StringComparer.Ordinal) { "leads:read" }));
        var inboxOne = await SeedInboxWithMemberAsync(userId);
        var inboxTwo = await SeedInboxWithMemberAsync(userId);
        await SeedInboxWithMemberAsync(Guid.NewGuid()); // của user khác, không được lẫn vào
        var resolver = CreateResolver();

        var ids = await resolver.GetInboxIdsAsync(PrincipalFor(userId, roleId), CancellationToken.None);

        ids.Should().BeEquivalentTo(new[] { inboxOne.Id, inboxTwo.Id });
    }

    [Fact]
    public async Task GetInboxIdsAsync_UserHasNoInboxMembers_ReturnsGuidEmptySentinel()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _permissions.GetPermissionsAsync(roleId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(StringComparer.Ordinal) { "leads:read" }));
        var resolver = CreateResolver();

        var ids = await resolver.GetInboxIdsAsync(PrincipalFor(userId, roleId), CancellationToken.None);

        ids.Should().Equal(Guid.Empty);
    }

    [Fact]
    public async Task GetInboxIdsAsync_UnparsableUserIdClaim_ReturnsGuidEmptySentinel()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "not-a-guid"),
            ],
            "Bearer");
        var resolver = CreateResolver();

        var ids = await resolver.GetInboxIdsAsync(new ClaimsPrincipal(identity), CancellationToken.None);

        ids.Should().Equal(Guid.Empty);
    }

    [Fact]
    public async Task GetInboxIdsAsync_CalledTwice_CachesResultAndDoesNotQueryAgain()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _permissions.GetPermissionsAsync(roleId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(StringComparer.Ordinal) { "leads:read" }));
        var inbox = await SeedInboxWithMemberAsync(userId);
        var resolver = CreateResolver();
        var principal = PrincipalFor(userId, roleId);

        var first = await resolver.GetInboxIdsAsync(principal, CancellationToken.None);
        // Xoá permission stub để nếu resolver gọi lại thì lần 2 sẽ trả rỗng (khác lần 1) và lộ bug.
        _permissions.GetPermissionsAsync(roleId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal)));
        var second = await resolver.GetInboxIdsAsync(principal, CancellationToken.None);

        first.Should().Equal(inbox.Id);
        second.Should().BeSameAs(first);
    }

    private sealed class StubTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
