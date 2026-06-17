using Clawbot.Domain.SaleAssist;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// Development-only seeder. The repo ships no EF migrations, so this provisions the
/// EF schema (including the Identity AspNet* tables that <see cref="RbacSeeder"/> and
/// login depend on), a default tenant, and a test admin user whose password is hashed
/// by Identity's <see cref="UserManager{TUser}"/> � so /auth/login works end to end.
/// Never wire this into production: use real migrations/DDL there instead.
/// </summary>
public static partial class DevDataSeeder
{
    public const string TenantSlug = "default";
    public const string AdminEmail = "admin@clawbot.local";
    public const string AdminPassword = "Admin@12345";
    public const string SaleEmail = "sale@clawbot.local";
    public const string SalePassword = "Sale@12345";

    /// <summary>
    /// Creates the database and EF schema if they do not yet exist. Builds a standalone
    /// <see cref="AppDbContext"/> so it can run BEFORE the web host is built � Hangfire
    /// installs its own schema during host build and needs the database to already exist.
    /// </summary>
    public static async Task EnsureSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new AppDbContext(options, NullTenantAccessor.Instance);
        await db.Database.EnsureCreatedAsync(ct);
    }

    // EnsureCreated only builds the model (the tenant query filter is an unexecuted
    // expression at that point), so a no-op accessor is sufficient here.
    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public static readonly NullTenantAccessor Instance = new();
        public TenantContext? Current => null;
        public TenantContext Require() => throw new NotSupportedException();
    }

    /// <summary>
    /// Idempotently provisions the default tenant and the test admin user. Run after
    /// <see cref="RbacSeeder.SeedAsync"/> so the Admin role already exists.
    /// </summary>
    public static async Task SeedAdminAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == TenantSlug, ct);
        if (tenant is null)
        {
            tenant = Tenant.Create(TenantSlug, "Default Tenant", "free", DateTimeOffset.UtcNow);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
        }

        var user = await users.FindByEmailAsync(AdminEmail);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = AdminEmail,
                Email = AdminEmail,
                EmailConfirmed = true,
                TenantId = tenant.Id,
                DisplayName = "Dev Admin",
                IsActive = true,
            };

            var result = await users.CreateAsync(user, AdminPassword);
            if (!result.Succeeded)
            {
                LogAdminSeedFailed(logger, AdminEmail,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }

            await EnsureAdminRoleAsync(users, user, logger);
            LogAdminSeeded(logger, AdminEmail);
            return;
        }

        user.UserName = AdminEmail;
        user.Email = AdminEmail;
        user.EmailConfirmed = true;
        user.TenantId = tenant.Id;
        user.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? "Dev Admin" : user.DisplayName;
        user.IsActive = true;

        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            LogAdminSeedFailed(logger, AdminEmail,
                string.Join("; ", update.Errors.Select(e => e.Description)));
            return;
        }

        await EnsureDefaultPasswordAsync(users, user, logger);
        await EnsureAdminRoleAsync(users, user, logger);
        LogAdminSeeded(logger, AdminEmail);
    }

    private static async Task EnsureDefaultPasswordAsync(UserManager<AppUser> users, AppUser user, ILogger logger)
    {
        if (await users.CheckPasswordAsync(user, AdminPassword)) return;

        IdentityResult result;
        if (await users.HasPasswordAsync(user))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            result = await users.ResetPasswordAsync(user, token, AdminPassword);
        }
        else
        {
            result = await users.AddPasswordAsync(user, AdminPassword);
        }

        if (!result.Succeeded)
        {
            LogAdminSeedFailed(logger, AdminEmail,
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task EnsureAdminRoleAsync(UserManager<AppUser> users, AppUser user, ILogger logger)
    {
        if (await users.IsInRoleAsync(user, RbacSeeder.Admin)) return;

        var result = await users.AddToRoleAsync(user, RbacSeeder.Admin);
        if (!result.Succeeded)
        {
            LogAdminSeedFailed(logger, AdminEmail,
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Idempotently provisions a demo sales account for development testing.
    /// </summary>
    public static async Task SeedSaleAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == TenantSlug, ct);
        if (tenant is null)
        {
            tenant = Tenant.Create(TenantSlug, "Default Tenant", "free", DateTimeOffset.UtcNow);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
        }

        if (await users.FindByEmailAsync(SaleEmail) is not null) return;

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = SaleEmail,
            Email = SaleEmail,
            EmailConfirmed = true,
            TenantId = tenant.Id,
            DisplayName = "Dev Sale",
        };

        var result = await users.CreateAsync(user, SalePassword);
        if (!result.Succeeded)
        {
            LogSaleSeedFailed(logger, SaleEmail,
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRoleAsync(user, "Sale");
        LogSaleSeeded(logger, SaleEmail);
    }

    /// <summary>
    /// Seeds a default auto_reply QuickReplyTemplate so demo auto-reply works
    /// out of the box without manual configuration.
    /// </summary>
    public static async Task SeedAutoReplyTemplateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == TenantSlug, ct);
        if (tenant is null)
        {
            LogNoTenantForAutoReply(logger);
            return;
        }

        // Ignore tenant filter to check existing templates
        if (await db.QuickReplyTemplates.IgnoreQueryFilters().AnyAsync(q => q.Code == "auto_reply", ct))
            return;

        var tpl = QuickReplyTemplate.Create(
            tenant.Id,
            "auto_reply",
            "Cảm ơn bạn đã liên hệ, chúng tôi sẽ phản hồi sớm",
            DateTimeOffset.UtcNow);
        db.QuickReplyTemplates.Add(tpl);
        try
        {
            await db.SaveChangesAsync(ct);
            LogAutoReplySeeded(logger);
        }
        catch (DbUpdateException)
        {
            // Idempotent: duplicate key means already seeded
        }
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "DevDataSeeder: seeded test admin {Email}")]
    private static partial void LogAdminSeeded(ILogger logger, string email);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "DevDataSeeder: failed to seed admin {Email}: {Errors}")]
    private static partial void LogAdminSeedFailed(ILogger logger, string email, string errors);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "DevDataSeeder: seeded test sale {Email}")]
    private static partial void LogSaleSeeded(ILogger logger, string email);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning,
        Message = "DevDataSeeder: failed to seed sale {Email}: {Errors}")]
    private static partial void LogSaleSeedFailed(ILogger logger, string email, string errors);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Information,
        Message = "DevDataSeeder: seeded auto_reply QuickReplyTemplate")]
    private static partial void LogAutoReplySeeded(ILogger logger);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Warning,
        Message = "DevDataSeeder: no tenant found, skipped auto_reply seeding")]
    private static partial void LogNoTenantForAutoReply(ILogger logger);
}
