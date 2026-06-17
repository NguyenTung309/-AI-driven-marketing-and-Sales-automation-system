using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Tests.Identity;

public sealed class DevDataSeederTests
{
    [Fact]
    public async Task SeedAdminAsync_repairs_existing_dev_admin_so_default_login_works()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var provider = BuildServices(connection);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            var tenant = Domain.Tenants.Tenant.Create(
                DevDataSeeder.TenantSlug,
                "Default Tenant",
                "free",
                DateTimeOffset.UtcNow);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var existing = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = DevDataSeeder.AdminEmail,
                Email = DevDataSeeder.AdminEmail,
                EmailConfirmed = false,
                TenantId = tenant.Id,
                DisplayName = "Existing Admin",
                IsActive = false,
            };

            var created = await users.CreateAsync(existing, "OldPass@12345");
            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        await RbacSeeder.SeedAsync(provider);
        await DevDataSeeder.SeedAdminAsync(provider);

        using var verifyScope = provider.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verifyUsers = verifyScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var seededTenant = await verifyDb.Tenants.SingleAsync(t => t.Slug == DevDataSeeder.TenantSlug);
        var admin = await verifyUsers.FindByEmailAsync(DevDataSeeder.AdminEmail);

        admin.Should().NotBeNull();
        admin!.TenantId.Should().Be(seededTenant.Id);
        admin.EmailConfirmed.Should().BeTrue();
        admin.IsActive.Should().BeTrue();
        (await verifyUsers.IsInRoleAsync(admin, RbacSeeder.Admin)).Should().BeTrue();
        (await verifyUsers.CheckPasswordAsync(admin, DevDataSeeder.AdminPassword)).Should().BeTrue();
    }

    private static ServiceProvider BuildServices(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddDataProtection();
        services.AddSingleton<ITenantAccessor>(NullTenantAccessor.Instance);
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connection)
                .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>());
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public static readonly NullTenantAccessor Instance = new();
        public TenantContext? Current => null;
        public TenantContext Require() => throw new NotSupportedException("No tenant in seed test.");
    }
}
