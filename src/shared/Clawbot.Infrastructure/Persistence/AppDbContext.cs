using System.Reflection;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Analytics;
using Clawbot.Domain.Channels;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Common;
using Clawbot.Domain.Competitors;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Content;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Domain.Experiments;
using Clawbot.Domain.Integrations;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Llm;
using Clawbot.Domain.Notifications;
using Clawbot.Domain.Observability;
using Clawbot.Domain.SaleAssist;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Identity;
using Clawbot.SharedKernel.Multitenancy;
using MassTransit;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Clawbot.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantAccessor tenants)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext
{
    private readonly ITenantAccessor _tenants = tenants;
    private Guid CurrentTenantId => _tenants.Current?.TenantId ?? Guid.Empty;

    // Tenants & Security
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Role> RbacRoles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemLogEntry> SystemLogs => Set<SystemLogEntry>();
    public DbSet<RequestStatsHourly> RequestStatsHourly => Set<RequestStatsHourly>();
    public DbSet<Auth.RefreshToken> RefreshTokens => Set<Auth.RefreshToken>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Clawbot.Domain.Jobs.BackgroundJob> BackgroundJobs => Set<Clawbot.Domain.Jobs.BackgroundJob>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<Clawbot.Domain.Notifications.PushSubscription> PushSubscriptions => Set<Clawbot.Domain.Notifications.PushSubscription>();

    // Contacts
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactExternalId> ContactExternalIds => Set<ContactExternalId>();

    // Conversations
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ConversationLabel> ConversationLabels => Set<ConversationLabel>();
    public DbSet<ConversationNote> ConversationNotes => Set<ConversationNote>();

    // Leads
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadActivity> LeadActivities => Set<LeadActivity>();
    public DbSet<LeadScoringRule> LeadScoringRules => Set<LeadScoringRule>();
    public DbSet<DripSequence> DripSequences => Set<DripSequence>();
    public DbSet<DripSequenceStep> DripSequenceSteps => Set<DripSequenceStep>();
    public DbSet<DripEnrollment> DripEnrollments => Set<DripEnrollment>();

    // Knowledge Base
    public DbSet<KbModule> KbModules => Set<KbModule>();
    public DbSet<KbVersion> KbVersions => Set<KbVersion>();
    public DbSet<SkillFile> SkillFiles => Set<SkillFile>();
    public DbSet<KbTestCase> KbTestCases => Set<KbTestCase>();
    public DbSet<KbSuggestion> KbSuggestions => Set<KbSuggestion>();
    public DbSet<ContactMemory> ContactMemories => Set<ContactMemory>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();

    // Chat scenarios
    public DbSet<ChatScenario> ChatScenarios => Set<ChatScenario>();
    public DbSet<Label> Labels => Set<Label>();

    // Agents
    public DbSet<AgentConfig> AgentConfigs => Set<AgentConfig>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<AgentA2AMessage> AgentA2AMessages => Set<AgentA2AMessage>();
    public DbSet<AgentSchedule> AgentSchedules => Set<AgentSchedule>();
    public DbSet<AgentScheduleRun> AgentScheduleRuns => Set<AgentScheduleRun>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<Inbox> Inboxes => Set<Inbox>();
    public DbSet<InboxMember> InboxMembers => Set<InboxMember>();
    public DbSet<AgentTrace> AgentTraces => Set<AgentTrace>();
    public DbSet<LlmCostEntry> LlmCostLedger => Set<LlmCostEntry>();

    // Sale Assist
    public DbSet<QuickReplyTemplate> QuickReplyTemplates => Set<QuickReplyTemplate>();
    public DbSet<UpsellSuggestionCache> UpsellSuggestionCaches => Set<UpsellSuggestionCache>();

    // Documents
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();

    // Content
    public DbSet<ContentBrief> ContentBriefs => Set<ContentBrief>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<ContentSchedule> ContentSchedules => Set<ContentSchedule>();
    public DbSet<ContentReviewTask> ContentReviewTasks => Set<ContentReviewTask>();
    public DbSet<ContentRenderTask> ContentRenderTasks => Set<ContentRenderTask>();
    public DbSet<ContentAsset> ContentAssets => Set<ContentAsset>();
    public DbSet<ContentPublishAttempt> ContentPublishAttempts => Set<ContentPublishAttempt>();
    public DbSet<ContentWorkflowMetricsHourly> ContentWorkflowMetricsHourly => Set<ContentWorkflowMetricsHourly>();
    public DbSet<ContentGenerationTrace> ContentGenerationTraces => Set<ContentGenerationTrace>();
    public DbSet<SocialCredential> SocialCredentials => Set<SocialCredential>();

    // Meta business integrations
    public DbSet<MetaConnection> MetaConnections => Set<MetaConnection>();
    public DbSet<MetaAsset> MetaAssets => Set<MetaAsset>();
    public DbSet<MetaOAuthState> MetaOAuthStates => Set<MetaOAuthState>();


    // Analytics
    public DbSet<KpiDaily> KpiDailies => Set<KpiDaily>();
    public DbSet<KpiForecast> KpiForecasts => Set<KpiForecast>();
    public DbSet<ReportArtifact> ReportArtifacts => Set<ReportArtifact>();

    // Experiments / A-B testing
    public DbSet<Experiment> Experiments => Set<Experiment>();
    public DbSet<ExperimentVariant> ExperimentVariants => Set<ExperimentVariant>();
    public DbSet<ExperimentAssignment> ExperimentAssignments => Set<ExperimentAssignment>();
    public DbSet<ExperimentEvent> ExperimentEvents => Set<ExperimentEvent>();

    // Channels & LLM configs
    public DbSet<PancakeConfig> PancakeConfigs => Set<PancakeConfig>();
    public DbSet<LlmConfig> LlmConfigs => Set<LlmConfig>();
    public DbSet<EmbeddingConfig> EmbeddingConfigs => Set<EmbeddingConfig>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    // Competitors
    public DbSet<CompetitorSource> CompetitorSources => Set<CompetitorSource>();
    public DbSet<CompetitorPost> CompetitorPosts => Set<CompetitorPost>();

    IConversationSet IAppDbContext.Conversations => new EfConversationSet(Conversations);

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefreshSqliteContentRenderTaskConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RefreshSqliteContentRenderTaskConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RefreshSqliteContentRenderTaskConcurrencyTokens()
    {
        if (!string.Equals(
                Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<ContentRenderTask>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(x => x.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }
    }

    private sealed class EfConversationSet(DbSet<Conversation> set) : IConversationSet
    {
        public void Add(Conversation conversation) => set.Add(conversation);

        public Task<Conversation?> FindByThreadAsync(string platform, string externalThreadId, CancellationToken ct = default) =>
            set.FirstOrDefaultAsync(c => c.Platform == platform && c.ExternalThreadId == externalThreadId, ct);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        if (string.Equals(Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            builder.Entity<ContentItem>().Property(x => x.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            builder.Entity<ContentSchedule>().Property(x => x.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            builder.Entity<ContentReviewTask>().Property(x => x.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            builder.Entity<ContentRenderTask>().Property(x => x.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            builder.Entity<ContentAsset>().Property(x => x.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            builder.Entity<ContentPublishAttempt>().Property(x => x.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
            builder.Entity<AgentSession>().Property(x => x.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
        }
        else
        {
            builder.Entity<ContentItem>().Property(x => x.RowVersion).IsRowVersion();
            builder.Entity<ContentSchedule>().Property(x => x.RowVersion).IsRowVersion();
            builder.Entity<ContentReviewTask>().Property(x => x.RowVersion).IsRowVersion();
            builder.Entity<ContentRenderTask>().Property(x => x.RowVersion).IsRowVersion();
            builder.Entity<ContentAsset>().Property(x => x.RowVersion).IsRowVersion();
            builder.Entity<ContentPublishAttempt>().Property(x => x.RowVersion).IsRowVersion();
            builder.Entity<AgentSession>().Property(x => x.RowVersion).IsRowVersion();
        }

        builder.AddInboxStateEntity();

        builder.Entity<InboxMember>(e =>
        {
            e.HasIndex(m => m.InboxId).IsUnique().HasDatabaseName("uq_inbox_members_inbox");
        });
        builder.AddOutboxMessageEntity();
        builder.AddOutboxStateEntity();

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var key in entity.GetKeys())
            {
                foreach (var property in key.Properties)
                {
                    if (property.ClrType == typeof(Guid))
                    {
                        property.ValueGenerated = ValueGenerated.Never;
                    }
                }
            }
        }

        builder.ApplySnakeCase();

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
                method.MakeGenericMethod(entity.ClrType).Invoke(this, [builder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantOwned
    {
        builder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }
}


