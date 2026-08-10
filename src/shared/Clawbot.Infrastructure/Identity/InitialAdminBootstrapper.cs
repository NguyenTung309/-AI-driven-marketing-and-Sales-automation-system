using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// Creates the first production administrator only when the Identity store is empty.
/// Credentials are supplied through the protected runtime configuration, never source control.
/// </summary>
public static class InitialAdminBootstrapper
{
    private const string SectionName = "Bootstrap";
    private const string DefaultTenantSlug = "default";

    public static async Task EnsureAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        if (await userManager.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
            return;

        var email = configuration[$"{SectionName}:InitialAdminEmail"];
        var password = configuration[$"{SectionName}:InitialAdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Bootstrap:InitialAdminEmail and Bootstrap:InitialAdminPassword are required when no administrator exists.");

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(item => item.Slug == DefaultTenantSlug, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The default tenant must exist before the initial administrator is created.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            TenantId = tenant.Id,
            DisplayName = "Administrator",
            IsActive = true,
        };

        var createResult = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!createResult.Succeeded)
            throw new InvalidOperationException("The initial administrator could not be created.");

        var roleResult = await userManager.AddToRoleAsync(user, RbacSeeder.Admin).ConfigureAwait(false);
        if (roleResult.Succeeded)
            return;

        await userManager.DeleteAsync(user).ConfigureAwait(false);
        throw new InvalidOperationException("The initial administrator could not be assigned the Administrator role.");
    }
}
