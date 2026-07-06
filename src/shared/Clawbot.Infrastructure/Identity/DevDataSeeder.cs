using Clawbot.Domain.Agents;
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
        await EnsureRuntimeSchemaAsync(db, ct).ConfigureAwait(false);
    }

    private static Task<int> EnsureRuntimeSchemaAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.users', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.users', N'pancake_access_token_encrypted') IS NULL
                ALTER TABLE dbo.users ADD pancake_access_token_encrypted NVARCHAR(2048) NULL;

            IF OBJECT_ID(N'dbo.users', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.users', N'pancake_access_token_updated_at') IS NULL
                ALTER TABLE dbo.users ADD pancake_access_token_updated_at DATETIMEOFFSET NULL;

            IF OBJECT_ID(N'dbo.inboxes', N'U') IS NULL AND OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL
                CREATE TABLE dbo.inboxes (
                    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_inboxes PRIMARY KEY DEFAULT NEWID(),
                    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id),
                    name NVARCHAR(256) NOT NULL,
                    platform NVARCHAR(32) NOT NULL,
                    external_page_id NVARCHAR(128) NOT NULL,
                    avatar_url NVARCHAR(512) NULL,
                    encrypted_access_token NVARCHAR(1024) NULL,
                    is_active BIT NOT NULL DEFAULT 1,
                    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
                    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
                    deleted_at DATETIMEOFFSET NULL);

            IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NULL
                ALTER TABLE dbo.inboxes ADD encrypted_access_token NVARCHAR(1024) NULL;

            IF OBJECT_ID(N'dbo.channel_tokens', N'U') IS NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL
                CREATE TABLE dbo.channel_tokens (
                    inbox_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_channel_tokens PRIMARY KEY REFERENCES dbo.inboxes(id),
                    access_token_encrypted NVARCHAR(MAX) NOT NULL,
                    refresh_token_encrypted NVARCHAR(MAX) NULL,
                    webhook_secret_encrypted NVARCHAR(MAX) NOT NULL,
                    token_expires_at DATETIMEOFFSET NULL,
                    is_active BIT NOT NULL DEFAULT 1,
                    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
                    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME());

            IF OBJECT_ID(N'dbo.inbox_members', N'U') IS NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL
                CREATE TABLE dbo.inbox_members (
                    inbox_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.inboxes(id),
                    agent_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id),
                    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id),
                    CONSTRAINT PK_inbox_members PRIMARY KEY (inbox_id, agent_id));

            IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL
                CREATE TABLE dbo.conversation_read_state (
                    user_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id),
                    conversation_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.conversations(id),
                    last_read_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT PK_conversation_read_state PRIMARY KEY (user_id, conversation_id));

            IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'inbox_id') IS NULL
                ALTER TABLE dbo.conversations ADD inbox_id UNIQUEIDENTIFIER NULL REFERENCES dbo.inboxes(id);

            IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'row_version') IS NULL
                ALTER TABLE dbo.conversations ADD row_version ROWVERSION;

            IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'snoozed_until') IS NULL
                ALTER TABLE dbo.conversations ADD snoozed_until DATETIMEOFFSET NULL;

            IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_inboxes_external' AND object_id = OBJECT_ID(N'dbo.inboxes'))
                CREATE INDEX ix_inboxes_external ON dbo.inboxes (tenant_id, platform, external_page_id) WHERE is_active = 1;

            IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_convread_conv' AND object_id = OBJECT_ID(N'dbo.conversation_read_state'))
                CREATE INDEX ix_convread_conv ON dbo.conversation_read_state (conversation_id);

            IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'attachment_url') IS NULL
                ALTER TABLE dbo.messages ADD attachment_url NVARCHAR(2048) NULL;

            IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_enabled') IS NULL
                ALTER TABLE dbo.conversations ADD ai_auto_reply_enabled BIT NOT NULL CONSTRAINT DF_conversations_ai_auto_reply_enabled DEFAULT 1;
            """, ct);

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

    // Orchestratable sub-agents the V2 coordinator plans over. Mirrors deploy/seed/agent-definitions.sql.
    // SPEC-16 P2-6: reviewer-agent is tool-capable (content.approve) so it can autonomously approve drafts;
    // content-agent is tool-capable (content-agent = persist draft) so the ReAct loop stores drafts, not just text.
    private static readonly (string Code, string DisplayName, string AgentType, string Persona, string AllowedToolsJson)[] OrchestratorAgents =
    [
        ("chat-agent",        "Chat Agent",        "chat",        "Handle customer conversation context and produce safe handoff summaries.", "[]"),
        ("sale-assist-agent", "Sale Assist Agent", "sale_assist", "Help sales users draft replies, summarize conversations, and suggest next steps.", "[]"),
        ("lead-agent",        "Lead Agent",        "lead",        "Score, classify, and prioritize leads from customer signals.", "[]"),
        ("content-agent",     "Content Agent",     "content",     "Create campaign content briefs and channel-ready drafts.", """["content-agent"]"""),
        ("research-agent",    "Research Agent",    "research",    "Research competitors, trends, and knowledge gaps for campaigns.", "[]"),
        ("docs-agent",        "Docs Agent",        "docs",        "Prepare quote, brochure, onboarding, and sales document artifacts.", "[]"),
        ("report-agent",      "Report Agent",      "report",      "Aggregate KPI, cost, and orchestration run outcomes into reports.", "[]"),
        ("ads-agent",         "Ads Agent",         "ads",         "Plan ad tasks and review campaign signals without publishing automatically.", "[]"),
        ("reviewer-agent",    "Reviewer Agent",    "reviewer",    "Review sub-agent outputs for quality, safety, and policy gates.", """["content.approve"]"""),
        ("publisher-agent",   "Publisher Agent",   "publisher",   "Schedule and publish approved content to social channels via the graph publisher.", """["content.schedule","content.publish"]"""),
        ("reporter-agent",    "Reporter Agent",    "reporter",    "Summarize A2A run outputs, decisions, costs, and next actions.", "[]"),
    ];

    /// <summary>
    /// Seeds the V2 orchestration sub-agent definitions for the default tenant so "Lập kế hoạch"
    /// has a catalog to plan over. Idempotent: inserts only the codes that are missing.
    /// </summary>
    public static async Task SeedAgentDefinitionsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == TenantSlug, ct);
        if (tenant is null)
            return;

        // Tracked (no AsNoTracking) so we can repair stale tool grants on existing rows, not just insert missing ones.
        var existing = await db.AgentDefinitions
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenant.Id)
            .ToListAsync(ct);
        var byCode = existing.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);

        var changed = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var (code, displayName, agentType, persona, allowedToolsJson) in OrchestratorAgents)
        {
            if (byCode.TryGetValue(code, out var def))
            {
                // Repair: a code seeded before tools were assigned (or before a grant changed) never received the
                // tools otherwise — the loop used to skip existing rows entirely, leaving them text-only.
                if (!string.Equals(def.AllowedToolsJson, allowedToolsJson, StringComparison.Ordinal))
                {
                    def.SetAllowedTools(allowedToolsJson, now);
                    changed++;
                }
                continue;
            }
            db.AgentDefinitions.Add(AgentDefinition.Create(
                tenant.Id, code, displayName, agentType, persona, now,
                allowedToolsJson: allowedToolsJson, memoryScope: "session", isOrchestratable: true));
            changed++;
        }

        if (changed == 0) return;
        try
        {
            await db.SaveChangesAsync(ct);
            LogAgentDefinitionsSeeded(logger, changed);
        }
        catch (DbUpdateException)
        {
            // Idempotent: a concurrent start inserted them first.
        }
    }

    public static async Task BackfillConversationInboxesAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");

        if (!await HasInboxBackfillSchemaAsync(db, ct))
        {
            LogBackfillSkippedMissingSchema(logger);
            return;
        }

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

    private static async Task<bool> HasInboxBackfillSchemaAsync(AppDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN
                    OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND
                    OBJECT_ID(N'dbo.inbox_members', N'U') IS NOT NULL AND
                    COL_LENGTH(N'dbo.conversations', N'inbox_id') IS NOT NULL AND
                    COL_LENGTH(N'dbo.conversations', N'row_version') IS NOT NULL AND
                    COL_LENGTH(N'dbo.conversations', N'snoozed_until') IS NOT NULL
                THEN 1 ELSE 0 END
                """;
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync().ConfigureAwait(false);
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

    [LoggerMessage(EventId = 1107, Level = LogLevel.Information,
        Message = "BackfillConversationInboxesAsync: skipped because inbox schema is not applied yet.")]
    private static partial void LogBackfillSkippedMissingSchema(ILogger logger);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning,
        Message = "DevDataSeeder: no tenant found, skipped auto_reply seeding")]
    private static partial void LogNoTenantForAutoReply(ILogger logger);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Information,
        Message = "DevDataSeeder: seeded {Count} orchestration agent_definitions")]
    private static partial void LogAgentDefinitionsSeeded(ILogger logger, int count);
}
