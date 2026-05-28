using System.Reflection;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Ads;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Analytics;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Common;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Content;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Domain.Leads;
using Clawbot.Domain.SaleAssist;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Identity;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantAccessor tenants)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext
{
    private readonly ITenantAccessor _tenants = tenants;

    // Tenants & Security
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Role> RbacRoles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

    // Knowledge Base
    public DbSet<KbModule> KbModules => Set<KbModule>();
    public DbSet<KbVersion> KbVersions => Set<KbVersion>();
    public DbSet<KbTestCase> KbTestCases => Set<KbTestCase>();

    // Chat scenarios
    public DbSet<ChatScenario> ChatScenarios => Set<ChatScenario>();

    // Agents
    public DbSet<AgentConfig> AgentConfigs => Set<AgentConfig>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<AgentTrace> AgentTraces => Set<AgentTrace>();

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

    // Analytics
    public DbSet<KpiDaily> KpiDailies => Set<KpiDaily>();

    IConversationSet IAppDbContext.Conversations => new EfConversationSet(Conversations);

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);

    private sealed class EfConversationSet(DbSet<Conversation> set) : IConversationSet
    {
        public void Add(Conversation conversation) => set.Add(conversation);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

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
        var tenantRef = _tenants;
        builder.Entity<TEntity>().HasQueryFilter(
            e => e.TenantId == (tenantRef.Current != null ? tenantRef.Current.TenantId : Guid.Empty));
    }
}
