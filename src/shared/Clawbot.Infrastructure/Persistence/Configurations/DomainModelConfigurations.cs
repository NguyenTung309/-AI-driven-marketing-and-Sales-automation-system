using Clawbot.Domain.Ads;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Analytics;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Content;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Domain.Leads;
using Clawbot.Domain.SaleAssist;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

// Consolidated configurations. Only table name + critical indexes/constraints.
// snake_case column mapping applied globally via SnakeCaseConventions.

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PlanName).HasColumnName("plan_name").HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.KeyHash).IsRequired();
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId });
    }
}

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(32);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Locale).HasMaxLength(16);
        builder.Property(x => x.LifecycleStage).HasMaxLength(32);
        builder.HasMany(x => x.ExternalIds).WithOne().HasForeignKey(e => e.ContactId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}

public sealed class ContactExternalIdConfiguration : IEntityTypeConfiguration<ContactExternalId>
{
    public void Configure(EntityTypeBuilder<ContactExternalId> builder)
    {
        builder.ToTable("contact_external_ids");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.Platform, x.ExternalId }).IsUnique();
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Direction).HasMaxLength(8).IsRequired();
        builder.Property(x => x.SenderType).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(32);
        builder.HasIndex(x => new { x.ConversationId, x.SentAt });
        builder.HasIndex(x => new { x.TenantId, x.SentAt });
    }
}

public sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Stage).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourcePlatform).HasMaxLength(32);
        builder.HasMany(x => x.Activities).WithOne().HasForeignKey(a => a.LeadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.Stage, x.Score });
    }
}

public sealed class LeadActivityConfiguration : IEntityTypeConfiguration<LeadActivity>
{
    public void Configure(EntityTypeBuilder<LeadActivity> builder)
    {
        builder.ToTable("lead_activities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActivityType).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.LeadId, x.OccurredAt });
    }
}

public sealed class LeadScoringRuleConfiguration : IEntityTypeConfiguration<LeadScoringRule>
{
    public void Configure(EntityTypeBuilder<LeadScoringRule> builder)
    {
        builder.ToTable("lead_scoring_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.EventCode });
    }
}

public sealed class KbModuleConfiguration : IEntityTypeConfiguration<KbModule>
{
    public void Configure(EntityTypeBuilder<KbModule> builder)
    {
        builder.ToTable("kb_modules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.KbModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.TestCases).WithOne().HasForeignKey(t => t.KbModuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class KbVersionConfiguration : IEntityTypeConfiguration<KbVersion>
{
    public void Configure(EntityTypeBuilder<KbVersion> builder)
    {
        builder.ToTable("kb_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.HasIndex(x => new { x.KbModuleId, x.Version }).IsUnique();
        // Embedding stored as JSON-serialized float array in NVARCHAR(MAX).
        // For vector similarity search use Qdrant — see IVectorStore.
        builder.Property(x => x.Embedding).HasColumnType("nvarchar(max)");
    }
}

public sealed class KbTestCaseConfiguration : IEntityTypeConfiguration<KbTestCase>
{
    public void Configure(EntityTypeBuilder<KbTestCase> builder)
    {
        builder.ToTable("kb_test_cases");
        builder.HasKey(x => x.Id);
    }
}

public sealed class ChatScenarioConfiguration : IEntityTypeConfiguration<ChatScenario>
{
    public void Configure(EntityTypeBuilder<ChatScenario> builder)
    {
        builder.ToTable("chat_scenarios");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.GroupName).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TriggerText).HasColumnName("trigger_text").IsRequired();
        builder.Property(x => x.Platforms).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ToneVoice).HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.GroupName });
    }
}

public sealed class AgentConfigConfiguration : IEntityTypeConfiguration<AgentConfig>
{
    public void Configure(EntityTypeBuilder<AgentConfig> builder)
    {
        builder.ToTable("agents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AgentType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.AgentType });
    }
}

public sealed class AgentSessionConfiguration : IEntityTypeConfiguration<AgentSession>
{
    public void Configure(EntityTypeBuilder<AgentSession> builder)
    {
        builder.ToTable("agent_sessions");
        builder.HasKey(x => x.Id);
        builder.HasMany(x => x.Traces).WithOne().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.StartedAt });
    }
}

public sealed class AgentTraceConfiguration : IEntityTypeConfiguration<AgentTrace>
{
    public void Configure(EntityTypeBuilder<AgentTrace> builder)
    {
        builder.ToTable("agent_traces");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SessionId, x.OccurredAt });
    }
}

public sealed class QuickReplyTemplateConfiguration : IEntityTypeConfiguration<QuickReplyTemplate>
{
    public void Configure(EntityTypeBuilder<QuickReplyTemplate> builder)
    {
        builder.ToTable("quick_reply_templates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> builder)
    {
        builder.ToTable("document_templates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DocType).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public sealed class GeneratedDocumentConfiguration : IEntityTypeConfiguration<GeneratedDocument>
{
    public void Configure(EntityTypeBuilder<GeneratedDocument> builder)
    {
        builder.ToTable("generated_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileUrl).HasMaxLength(512).IsRequired();
        builder.Property(x => x.SentVia).HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}

public sealed class ContentBriefConfiguration : IEntityTypeConfiguration<ContentBrief>
{
    public void Configure(EntityTypeBuilder<ContentBrief> builder)
    {
        builder.ToTable("content_briefs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

public sealed class ContentItemConfiguration : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.ToTable("content_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
    }
}

public sealed class ContentScheduleConfiguration : IEntityTypeConfiguration<ContentSchedule>
{
    public void Configure(EntityTypeBuilder<ContentSchedule> builder)
    {
        builder.ToTable("content_schedule");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PostUrl).HasMaxLength(512);
        builder.HasIndex(x => new { x.TenantId, x.ScheduledAt });
    }
}

public sealed class AdsCampaignConfiguration : IEntityTypeConfiguration<AdsCampaign>
{
    public void Configure(EntityTypeBuilder<AdsCampaign> builder)
    {
        builder.ToTable("ads_campaigns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalCampaignId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.Platform, x.ExternalCampaignId }).IsUnique();
    }
}

public sealed class AdsRuleConfiguration : IEntityTypeConfiguration<AdsRule>
{
    public void Configure(EntityTypeBuilder<AdsRule> builder)
    {
        builder.ToTable("ads_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Metric).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Comparator).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

public sealed class AdsActionConfiguration : IEntityTypeConfiguration<AdsAction>
{
    public void Configure(EntityTypeBuilder<AdsAction> builder)
    {
        builder.ToTable("ads_actions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActionTaken).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.CampaignId, x.ExecutedAt });
    }
}

public sealed class KpiDailyConfiguration : IEntityTypeConfiguration<KpiDaily>
{
    public void Configure(EntityTypeBuilder<KpiDaily> builder)
    {
        builder.ToTable("kpi_daily");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Date, x.Platform }).IsUnique();
    }
}
