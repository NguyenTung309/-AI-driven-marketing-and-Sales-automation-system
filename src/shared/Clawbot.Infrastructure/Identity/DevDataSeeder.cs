using Clawbot.Domain.Channels;
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

    public static async Task BackfillConversationInboxesAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var conversationsToFix = await db.Conversations
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        if (conversationsToFix.Count == 0) return;

        LogConversationsToResolve(logger, conversationsToFix.Count);

        var changed = false;
        foreach (var conv in conversationsToFix)
        {
            Guid? resolvedInboxId = null;

            var inboxes = await db.Inboxes
                .IgnoreQueryFilters()
                .Where(i => i.TenantId == conv.TenantId && i.Platform == conv.Platform)
                .ToListAsync(ct);

            var matchedInboxes = new List<Inbox>();
            foreach (var inbox in inboxes)
            {
                if (IsPageIdMatch(conv.ExternalThreadId, inbox.ExternalPageId))
                {
                    matchedInboxes.Add(inbox);
                }
            }

            if (matchedInboxes.Count > 0)
            {
                if (matchedInboxes.Count == 1)
                {
                    resolvedInboxId = matchedInboxes[0].Id;
                }
                else
                {
                    var matchedInboxIds = matchedInboxes.Select(i => i.Id).ToList();
                    var inboxesWithMembers = await db.InboxMembers
                        .IgnoreQueryFilters()
                        .Where(m => matchedInboxIds.Any(id => id == m.InboxId))
                        .Select(m => m.InboxId)
                        .Distinct()
                        .ToListAsync(ct);

                    if (inboxesWithMembers.Count > 0)
                    {
                        resolvedInboxId = matchedInboxes.First(i => inboxesWithMembers.Any(id => id == i.Id)).Id;
                    }
                    else
                    {
                        resolvedInboxId = matchedInboxes[0].Id;
                    }
                }
            }

            if (resolvedInboxId == null && inboxes.Count == 1)
            {
                resolvedInboxId = inboxes[0].Id;
            }

            if (resolvedInboxId != null && conv.InboxId != resolvedInboxId)
            {
                conv.SetInboxId(resolvedInboxId.Value);
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
            LogBackfillSuccess(logger);
        }
    }

    private static bool IsPageIdMatch(string externalThreadId, string externalPageId)
    {
        if (string.IsNullOrWhiteSpace(externalThreadId) || string.IsNullOrWhiteSpace(externalPageId))
            return false;

        if (externalThreadId.Contains(externalPageId, StringComparison.OrdinalIgnoreCase))
            return true;

        var pageDigits = new string(externalPageId.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(pageDigits) && externalThreadId.Contains(pageDigits, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "DevDataSeeder: seeded test admin {Email}")]
    private static partial void LogAdminSeeded(ILogger logger, string email);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "DevDataSeeder: failed to seed admin {Email}: {Errors}")]
    private static partial void LogAdminSeedFailed(ILogger logger, string email, string errors);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "DevDataSeeder: seeded auto_reply QuickReplyTemplate")]
    private static partial void LogAutoReplySeeded(ILogger logger);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Information,
        Message = "BackfillConversationInboxesAsync: Found {Count} conversations with NULL InboxId to resolve.")]
    private static partial void LogConversationsToResolve(ILogger logger, int count);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Information,
        Message = "BackfillConversationInboxesAsync: Successfully backfilled InboxId for conversations.")]
    private static partial void LogBackfillSuccess(ILogger logger);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning,
        Message = "DevDataSeeder: no tenant found, skipped auto_reply seeding")]
    private static partial void LogNoTenantForAutoReply(ILogger logger);
}
