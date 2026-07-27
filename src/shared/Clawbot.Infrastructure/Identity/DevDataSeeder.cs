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

            IF OBJECT_ID(N'dbo.contacts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.contacts', N'avatar_url') IS NULL
                ALTER TABLE dbo.contacts ADD avatar_url NVARCHAR(512) NULL;

            IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'sender_display_name') IS NULL
                ALTER TABLE dbo.messages ADD sender_display_name NVARCHAR(256) NULL;

            IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'sender_avatar_url') IS NULL
                ALTER TABLE dbo.messages ADD sender_avatar_url NVARCHAR(512) NULL;

            IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'attachment_url') IS NULL
                ALTER TABLE dbo.messages ADD attachment_url NVARCHAR(2048) NULL;

            IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_enabled') IS NULL
                ALTER TABLE dbo.conversations ADD ai_auto_reply_enabled BIT NOT NULL CONSTRAINT DF_conversations_ai_auto_reply_enabled DEFAULT 1;

            IF OBJECT_ID(N'dbo.meta_connections', N'U') IS NULL AND OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL
                CREATE TABLE dbo.meta_connections (
                    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_meta_connections PRIMARY KEY,
                    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id),
                    client_business_id NVARCHAR(128) NOT NULL,
                    system_user_id NVARCHAR(128) NOT NULL,
                    token_type NVARCHAR(64) NOT NULL,
                    access_token_encrypted NVARCHAR(MAX) NOT NULL,
                    granted_scopes_json NVARCHAR(MAX) NOT NULL,
                    expires_at DATETIMEOFFSET NULL,
                    data_access_expires_at DATETIMEOFFSET NULL,
                    last_validated_at DATETIMEOFFSET NULL,
                    status NVARCHAR(32) NOT NULL,
                    last_error NVARCHAR(1024) NULL,
                    created_at DATETIMEOFFSET NOT NULL,
                    updated_at DATETIMEOFFSET NOT NULL,
                    CONSTRAINT UQ_meta_connections_tenant UNIQUE (tenant_id));

            IF OBJECT_ID(N'dbo.meta_assets', N'U') IS NULL AND OBJECT_ID(N'dbo.meta_connections', N'U') IS NOT NULL
                CREATE TABLE dbo.meta_assets (
                    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_meta_assets PRIMARY KEY,
                    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id),
                    connection_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.meta_connections(id) ON DELETE CASCADE,
                    asset_type NVARCHAR(32) NOT NULL,
                    external_id NVARCHAR(128) NOT NULL,
                    name NVARCHAR(256) NOT NULL,
                    tasks_json NVARCHAR(MAX) NOT NULL,
                    access_token_encrypted NVARCHAR(MAX) NOT NULL,
                    is_default BIT NOT NULL DEFAULT 0,
                    is_active BIT NOT NULL DEFAULT 1,
                    last_synced_at DATETIMEOFFSET NOT NULL,
                    created_at DATETIMEOFFSET NOT NULL,
                    updated_at DATETIMEOFFSET NOT NULL,
                    CONSTRAINT UQ_meta_assets_tenant_type_external UNIQUE (tenant_id, asset_type, external_id));

            IF OBJECT_ID(N'dbo.meta_oauth_states', N'U') IS NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL
                CREATE TABLE dbo.meta_oauth_states (
                    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_meta_oauth_states PRIMARY KEY,
                    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id),
                    user_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id),
                    state_hash NVARCHAR(64) NOT NULL CONSTRAINT UQ_meta_oauth_states_hash UNIQUE,
                    expires_at DATETIMEOFFSET NOT NULL,
                    consumed_at DATETIMEOFFSET NULL,
                    created_at DATETIMEOFFSET NOT NULL);

            IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.content_schedule', N'meta_asset_id') IS NULL
                ALTER TABLE dbo.content_schedule ADD meta_asset_id UNIQUEIDENTIFIER NULL;

            IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.meta_assets', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.content_schedule', N'meta_asset_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE name = N'FK_content_schedule_meta_assets'
                     AND parent_object_id = OBJECT_ID(N'dbo.content_schedule'))
                ALTER TABLE dbo.content_schedule ADD CONSTRAINT FK_content_schedule_meta_assets
                    FOREIGN KEY (meta_asset_id) REFERENCES dbo.meta_assets(id) ON DELETE SET NULL;

            IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.content_schedule', N'meta_asset_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE name = N'IX_content_schedule_meta_asset_id'
                     AND object_id = OBJECT_ID(N'dbo.content_schedule'))
                CREATE INDEX IX_content_schedule_meta_asset_id ON dbo.content_schedule(meta_asset_id);

            IF OBJECT_ID(N'dbo.system_logs', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.system_logs (
                    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_system_logs PRIMARY KEY,
                    occurred_at DATETIMEOFFSET NOT NULL,
                    level NVARCHAR(16) NOT NULL,
                    source NVARCHAR(32) NOT NULL,
                    category NVARCHAR(256) NULL,
                    message NVARCHAR(2048) NOT NULL,
                    exception NVARCHAR(MAX) NULL,
                    status_code INT NULL,
                    method NVARCHAR(10) NULL,
                    path NVARCHAR(512) NULL,
                    elapsed_ms FLOAT NULL,
                    trace_id NVARCHAR(64) NULL,
                    tenant_id UNIQUEIDENTIFIER NULL,
                    user_id UNIQUEIDENTIFIER NULL,
                    properties NVARCHAR(MAX) NULL
                );
                CREATE INDEX ix_system_logs_occurred ON dbo.system_logs(occurred_at DESC) INCLUDE (level, tenant_id);
                CREATE INDEX ix_system_logs_tenant ON dbo.system_logs(tenant_id, occurred_at DESC);
                CREATE INDEX ix_system_logs_trace ON dbo.system_logs(trace_id) WHERE trace_id IS NOT NULL;
            END

            IF OBJECT_ID(N'dbo.request_stats_hourly', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.request_stats_hourly (
                    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_request_stats_hourly PRIMARY KEY,
                    bucket_hour DATETIMEOFFSET NOT NULL,
                    tenant_id UNIQUEIDENTIFIER NOT NULL,
                    status_class NVARCHAR(8) NOT NULL,
                    count BIGINT NOT NULL CONSTRAINT df_request_stats_hourly_count DEFAULT 0
                );
                CREATE UNIQUE INDEX ux_request_stats_hourly_bucket_tenant_class
                    ON dbo.request_stats_hourly(bucket_hour, tenant_id, status_class);
                CREATE INDEX ix_request_stats_hourly_tenant_bucket
                    ON dbo.request_stats_hourly(tenant_id, bucket_hour DESC);
            END
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
    // SPEC-16 P2-6 / Phase 4.9: reviewer-agent uses content.review (non-publishing durable review).
    // content-agent is tool-capable (content-agent = persist draft) so the ReAct loop stores drafts, not just text.
    // Tool grants must match ToolRegistry names (adapter Name or explicit IAgentTool.Name).
    // Empty [] = text-only ReAct (no tool call) — BAD for lead/sale/report/research when goal needs CRM/data.
    private static readonly (string Code, string DisplayName, string AgentType, string Persona, string AllowedToolsJson)[] OrchestratorAgents =
    [
        ("chat-agent",        "Chat Agent",        "chat",        "Handle customer conversation context and produce safe handoff summaries. Call chat-agent tool when a reply is needed.", """["chat-agent"]"""),
        ("sale-assist-agent", "Sale Assist Agent", "sale_assist", "Draft/summarize/upsell for an EXISTING conversation. Call sale-assist with conversation_id + turns_json. Cannot blast cold leads without conversation context.", """["sale-assist"]"""),
        ("lead-agent",        "Lead Agent",        "lead",        "ALWAYS call lead-agent tool. Use operation=list|find_cold (stage, topN) to query CRM — do not invent lists or ask the user for lead IDs. Also score/create/batch_score.", """["lead-agent"]"""),
        ("content-agent",     "Content Agent",     "content",     "Create campaign content briefs and channel-ready drafts.", """["content-agent"]"""),
        ("research-agent",    "Research Agent",    "research",    "Research competitors, trends, and knowledge gaps. Call research-agent or web.search with geo/keywords.", """["research-agent","web.search"]"""),
        ("docs-agent",        "Docs Agent",        "docs",        "Prepare quote, brochure, onboarding documents via docs-agent tool.", """["docs-agent"]"""),
        ("report-agent",      "Report Agent",      "report",      "Aggregate KPI via report-agent tool (snapshot/anomaly/forecast). Pass date/platform/metric as needed.", """["report-agent"]"""),
        ("ads-agent",         "Ads Agent",         "ads",         "Plan ad tasks via ads-agent tool. High-risk actions may require approval.", """["ads-agent"]"""),
        ("reviewer-agent",    "Reviewer Agent",    "reviewer",    "Review sub-agent outputs for quality, safety, and policy gates.", """["content.review"]"""),
        ("publisher-agent",   "Publisher Agent",   "publisher",   "Publishing is handled by the durable worker; no autonomous publishing tools are granted by default.", "[]"),
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
