using Clawbot.Api.Endpoints;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class AdminUsersEndpointsTests
{
    [Fact]
    public async Task QueryUsersAsync_ReturnsSortedIdentityRolesAndEmptyArrayForUnassignedUsers()
    {
        // Arrange
        await using var fixture = await AdminUsersFixture.CreateAsync();
        var alpha = fixture.CreateUser("Alpha", "alpha@example.test");
        var beta = fixture.CreateUser("Beta", "beta@example.test");
        var otherTenantUser = fixture.CreateUser("Other tenant", "other@example.test", Guid.NewGuid());
        var admin = AdminUsersFixture.CreateRole("Admin");
        var salesLead = AdminUsersFixture.CreateRole("SalesLead");
        var viewer = AdminUsersFixture.CreateRole("Viewer");

        fixture.Db.AddRange(alpha, beta, otherTenantUser, admin, salesLead, viewer);
        fixture.Db.UserRoles.AddRange(
            new IdentityUserRole<Guid> { UserId = alpha.Id, RoleId = salesLead.Id },
            new IdentityUserRole<Guid> { UserId = alpha.Id, RoleId = admin.Id },
            new IdentityUserRole<Guid> { UserId = otherTenantUser.Id, RoleId = viewer.Id });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        // Act
        var result = await AdminUsersEndpoints.QueryUsersAsync(
            fixture.Db,
            fixture.TenantId,
            queryText: null,
            page: 1,
            pageSize: 50,
            includeRoles: true,
            CancellationToken.None);

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Select(user => user.Email).Should().NotContain(otherTenantUser.Email);

        var alphaResult = result.Items.Single(user => user.Email == alpha.Email);
        alphaResult.Roles.Should().Equal("Admin", "SalesLead");

        var betaResult = result.Items.Single(user => user.Email == beta.Email);
        betaResult.Roles.Should().NotBeNull();
        betaResult.Roles!.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryUsersAsync_ExcludesCrossTenantPancakeChannels()
    {
        // Arrange
        await using var fixture = await AdminUsersFixture.CreateAsync();
        var user = fixture.CreateUser("Alpha", "alpha@example.test");
        var foreignInbox = Inbox.Create(Guid.NewGuid(), "Foreign inbox", "zalo", "foreign-page");
        fixture.Db.AddRange(user, foreignInbox);
        fixture.Db.InboxMembers.Add(InboxMember.Create(fixture.TenantId, foreignInbox.Id, user.Id));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        // Act
        var result = await AdminUsersEndpoints.QueryUsersAsync(
            fixture.Db,
            fixture.TenantId,
            queryText: null,
            page: 1,
            pageSize: 50,
            includeRoles: true,
            CancellationToken.None);

        // Assert
        result.Items.Should().ContainSingle();
        result.Items[0].PancakeChannels.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryUsersAsync_OmitsRoleMetadataWhenCallerCannotManageUsers()
    {
        // Arrange
        await using var fixture = await AdminUsersFixture.CreateAsync();
        var user = fixture.CreateUser("Alpha", "alpha@example.test");
        var admin = AdminUsersFixture.CreateRole("Admin");
        fixture.Db.AddRange(user, admin);
        fixture.Db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = admin.Id });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        // Act
        var result = await AdminUsersEndpoints.QueryUsersAsync(
            fixture.Db,
            fixture.TenantId,
            queryText: null,
            page: 1,
            pageSize: 50,
            includeRoles: false,
            CancellationToken.None);

        // Assert
        result.Items.Should().ContainSingle();
        result.Items[0].Roles.Should().BeNull();
    }

    private sealed class AdminUsersFixture(SqliteConnection connection, AppDbContext db, Guid tenantId) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public Guid TenantId { get; } = tenantId;

        public static async Task<AdminUsersFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var tenantId = Guid.NewGuid();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, new TestTenantAccessor(tenantId));
            await db.Database.EnsureCreatedAsync();
            return new AdminUsersFixture(connection, db, tenantId);
        }

        public AppUser CreateUser(string displayName, string email, Guid? tenantId = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId ?? TenantId,
                DisplayName = displayName,
                Email = email,
                UserName = email,
                NormalizedEmail = email.ToUpperInvariant(),
                NormalizedUserName = email.ToUpperInvariant(),
                IsActive = true,
            };

        public static AppRole CreateRole(string name) =>
            new(name)
            {
                Id = Guid.NewGuid(),
                NormalizedName = name.ToUpperInvariant(),
            };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
