using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// Idempotently provisions Identity roles (Admin/Sale/Marketer/QA/Viewer) used by
/// JWT issue. Custom tenant-scoped Role rows are created per tenant on demand by
/// RolesEndpoints.
/// </summary>
public static partial class RbacSeeder
{
    public static readonly IReadOnlyList<string> DefaultRoles =
        new[] { "Admin", "Sale", "Marketer", "QA", "Viewer" };

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var roleManager = sp.GetRequiredService<RoleManager<AppRole>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("RbacSeeder");

        foreach (var name in DefaultRoles)
        {
            if (await roleManager.RoleExistsAsync(name)) continue;
            var role = new AppRole(name) { Id = Guid.NewGuid() };
            var result = await roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                LogRoleSeedFailed(logger, name,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        var db = sp.GetRequiredService<AppDbContext>();
        var count = await db.Permissions.CountAsync(ct);
        LogPermissionCount(logger, count);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Failed to seed role {RoleName}: {Errors}")]
    private static partial void LogRoleSeedFailed(ILogger logger, string roleName, string errors);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "RbacSeeder: {Count} permissions registered")]
    private static partial void LogPermissionCount(ILogger logger, int count);
}
