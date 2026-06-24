using System.Reflection;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Ads;
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
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Llm;
using Clawbot.Domain.Notifications;
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
    public DbSet<Auth.RefreshToken> RefreshTokens => Set<Auth.RefreshToken>();
    public DbSet<Notification> Notifications => Set<Notification>();

    // Contacts
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactExternalId> ContactExternalIds => Set<ContactExternalId>();

    // Conversations
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();

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
    public DbSet<KbTestCase> KbTestCases => Set<KbTestCase>();

    // Chat scenarios
    public DbSet<ChatScenario> ChatScenarios => Set<ChatScenario>();

    // Agents
    public DbSet<AgentConfig> AgentConfigs => Set<AgentConfig>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<AgentA2AMessage> AgentA2AMessages => Set<AgentA2AMessage>();
    public DbSet<AgentSchedule> AgentSchedules => Set<AgentSchedule>();
    public DbSet<AgentScheduleRun> AgentScheduleRuns => Set<AgentScheduleRun>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<AgentTrace> AgentTraces => Set<AgentTrace>();
    public DbSet<ClaudeCostEntry> ClaudeCostLedger => Set<ClaudeCostEntry>();

    // Sale Assist
    public DbSet<QuickReplyTemplate> QuickReplyTemplates => Set<QuickReplyTemplate>();

    // Documents
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();

    // Content
    public DbSet<ContentBrief> ContentBriefs => Set<ContentBrief>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<ContentSchedule> ContentSchedules => Set<ContentSchedule>();

    // Ads
    public DbSet<AdsCampaign> AdsCampaigns => Set<AdsCampaign>();
    public DbSet<AdsRule> AdsRules => Set<AdsRule>();
    public DbSet<AdsAction> AdsActions => Set<AdsAction>();
    public DbSet<AdsCreative> AdsCreatives => Set<AdsCreative>();
    public DbSet<AdsMetricsDaily> AdsMetricsDailies => Set<AdsMetricsDaily>();

    // Analytics
    public DbSet<KpiDaily> KpiDailies => Set<KpiDaily>();
    public DbSet<KpiForecast> KpiForecasts => Set<KpiForecast>();

    // Experiments / A-B testing
    public DbSet<Experiment> Experiments => Set<Experiment>();
    public DbSet<ExperimentVariant> ExperimentVariants => Set<ExperimentVariant>();
    public DbSet<ExperimentAssignment> ExperimentAssignments => Set<ExperimentAssignment>();
    public DbSet<ExperimentEvent> ExperimentEvents => Set<ExperimentEvent>();

    // Channels & LLM configs
    public DbSet<PancakeConfig> PancakeConfigs => Set<PancakeConfig>();
    public DbSet<LlmConfig> LlmConfigs => Set<LlmConfig>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    // Competitors
    public DbSet<CompetitorSource> CompetitorSources => Set<CompetitorSource>();
    public DbSet<CompetitorPost> CompetitorPosts => Set<CompetitorPost>();

    IConversationSet IAppDbContext.Conversations => new EfConversationSet(Conversations);

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);

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

        builder.AddInboxStateEntity();
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
