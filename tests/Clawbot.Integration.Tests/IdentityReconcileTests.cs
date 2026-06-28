using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clawbot.Integration.Tests;

// M23 — verifies the Identity↔DDL reconcile (0013/0014): AppUser maps to the DDL `users` table
// with all Identity columns present. Runs against the real SQL Server (Testcontainers) so it
// catches DDL/EF mismatches that EnsureCreated-based unit tests cannot.
public sealed class IdentityReconcileTests : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    private readonly ClawbotWebApplicationFactory _factory;

    public IdentityReconcileTests(SqlServerFixture sql) => _factory = new ClawbotWebApplicationFactory(sql);

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
    }
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task UserManager_creates_and_finds_user_against_ddl_users_table()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.FirstAsync();
        var tenantId = tenant.Id;

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var email = $"recon-{Guid.NewGuid():N}@hoc-ba.edu.vn";
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            UserName = email,
            DisplayName = "Reconcile Test",
            IsActive = true,
        };

        // CreateAsync writes every Identity column (normalized_email, concurrency_stamp,
        // two_factor_enabled, lockout_enabled, …) → fails if 0013 missed one.
        var created = await users.CreateAsync(user, "P@ssw0rd!");
        created.Succeeded.Should().BeTrue(because: string.Join("; ", created.Errors.Select(e => e.Description)));

        // FindByEmailAsync reads normalized_email → exercises that column + its index.
        var found = await users.FindByEmailAsync(email);
        found.Should().NotBeNull();
        found!.DisplayName.Should().Be("Reconcile Test");
        found.IsActive.Should().BeTrue();
    }
}
