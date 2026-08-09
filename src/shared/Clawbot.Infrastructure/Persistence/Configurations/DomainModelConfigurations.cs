using Clawbot.Domain.Ads;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Analytics;
using Clawbot.Domain.Channels;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Content;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Domain.Experiments;
using Clawbot.Domain.Integrations;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Llm;
using Clawbot.Domain.SaleAssist;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Audit;
using Clawbot.Infrastructure.Identity;
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
        builder.Property(x => x.BrandName).HasMaxLength(256);
        builder.Property(x => x.LogoUrl).HasMaxLength(512);
        builder.Property(x => x.PrimaryColor).HasMaxLength(16);
        builder.Property(x => x.AccentColor).HasMaxLength(16);
        builder.Property(x => x.SupportName).HasMaxLength(256);
        builder.Property(x => x.WidgetGreeting).HasMaxLength(1024);
        builder.Property(x => x.RequireOrchestrationApproval).HasDefaultValue(false);
        builder.Property(x => x.RequireContentReview).HasDefaultValue(false);
        builder.Property(x => x.ContentPublishingApprovalPolicy)
            .HasMaxLength(32)
            .HasDefaultValue(Tenant.ContentPublishingPolicyHumanRequired)
            .IsRequired();
        builder.Property(x => x.ContentPublishingPolicyVersion).HasDefaultValue(1L);
        builder.Property(x => x.ContentPublishingPolicyUpdatedAt).IsRequired();
        builder.Property(x => x.RequireChatReplyApproval).HasDefaultValue(false);
        builder.Property(x => x.SkipChatReplyReview).HasDefaultValue(false);
        builder.Property(x => x.MonthlyCostCapUsd).HasColumnType("decimal(12,2)");
        builder.Property(x => x.AiAutoReplyResumeMinutes).HasColumnName("ai_auto_reply_resume_minutes").HasDefaultValue(5);
        builder.Property(x => x.IdleAlertMinutes).HasColumnName("idle_alert_minutes").HasDefaultValue(5);
        builder.Property(x => x.LeadLostAfterDays)
            .HasColumnName("lead_lost_after_days")
            .HasDefaultValue(60)
            .ValueGeneratedNever();
        builder.Property(x => x.AutoApproveLeadRevenue).HasColumnName("auto_approve_lead_revenue").HasDefaultValue(false);
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

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(x => new { x.RoleId, x.PermissionId });
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.NoAction);
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
        builder.Property(x => x.ScopesJson).HasColumnName("scopes_json").IsRequired();
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
        builder.Property(x => x.UserAgent).HasMaxLength(HttpAuditContext.MaxUserAgentLength);
        builder.Property(x => x.EventKey).HasMaxLength(256);
        builder.Property(x => x.IpAddress)
            .HasConversion(
                v => v == null ? null : v.ToString(),
                v => string.IsNullOrEmpty(v) ? null : System.Net.IPAddress.Parse(v))
            .HasMaxLength(45);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId });
        builder.HasIndex(x => new { x.TenantId, x.EventKey })
            .IsUnique()
            .HasFilter("[event_key] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.ResourceId, x.StateSequence })
            .HasFilter("[state_sequence] IS NOT NULL");
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
        builder.Property(x => x.AttachmentUrl).HasMaxLength(2048);
        builder.Property(x => x.ParentCommentId).HasColumnName("parent_comment_id").HasMaxLength(256);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired().HasDefaultValue("sent");
        builder.HasIndex(x => new { x.ConversationId, x.SentAt });
        builder.HasIndex(x => new { x.TenantId, x.SentAt });
        builder.HasIndex(x => new { x.TenantId, x.ParentCommentId });
    }
}

public sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Stage).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        // Cùng Stage: inbound TouchInbound chỉ đổi LastActivityAt — token này chặn auto-lost ghi đè lead vừa nhắn.
        builder.Property(x => x.LastActivityAt).IsConcurrencyToken();
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

public sealed class LeadRevenueConfiguration : IEntityTypeConfiguration<LeadRevenue>
{
    public void Configure(EntityTypeBuilder<LeadRevenue> builder)
    {
        builder.ToTable("lead_revenues");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.Evidence).HasMaxLength(1000);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        builder.HasIndex(x => x.LeadId);
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

public sealed class DripSequenceConfiguration : IEntityTypeConfiguration<DripSequence>
{
    public void Configure(EntityTypeBuilder<DripSequence> builder)
    {
        builder.ToTable("drip_sequences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TriggerEvent).HasMaxLength(64).IsRequired();
        builder.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.SequenceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.TriggerEvent });
    }
}

public sealed class DripSequenceStepConfiguration : IEntityTypeConfiguration<DripSequenceStep>
{
    public void Configure(EntityTypeBuilder<DripSequenceStep> builder)
    {
        builder.ToTable("drip_sequence_steps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Channel).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TemplateBody).IsRequired();
        builder.HasIndex(x => new { x.SequenceId, x.StepOrder }).IsUnique();
    }
}

public sealed class DripEnrollmentConfiguration : IEntityTypeConfiguration<DripEnrollment>
{
    public void Configure(EntityTypeBuilder<DripEnrollment> builder)
    {
        builder.ToTable("drip_enrollments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasOne<DripSequence>().WithMany().HasForeignKey(x => x.SequenceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.SequenceId, x.LeadId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextSendAt });
        builder.HasIndex(x => new { x.TenantId, x.Status });
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

public sealed class SkillFileConfiguration : IEntityTypeConfiguration<SkillFile>
{
    public void Configure(EntityTypeBuilder<SkillFile> builder)
    {
        builder.ToTable("skill_files");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.ContentMd).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("[deleted_at] IS NULL");
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
        // For vector similarity search use Qdrant - see IVectorStore.
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

public sealed class AgentMemoryConfiguration : IEntityTypeConfiguration<AgentMemory>
{
    public void Configure(EntityTypeBuilder<AgentMemory> builder)
    {
        builder.ToTable("agent_memories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Fact).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Confidence).HasPrecision(3, 2);
        builder.HasIndex(x => new { x.TenantId, x.AgentCode, x.IsActive });
    }
}

public sealed class ContactMemoryConfiguration : IEntityTypeConfiguration<Clawbot.Domain.Contacts.ContactMemory>
{
    public void Configure(EntityTypeBuilder<Clawbot.Domain.Contacts.ContactMemory> builder)
    {
        builder.ToTable("contact_memories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Fact).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Confidence).HasPrecision(3, 2);
        // Truy vấn nóng của ChatAgent: facts active của 1 khách, mới nhất trước.
        builder.HasIndex(x => new { x.TenantId, x.ContactId, x.IsActive });
    }
}

public sealed class KbSuggestionConfiguration : IEntityTypeConfiguration<KbSuggestion>
{
    public void Configure(EntityTypeBuilder<KbSuggestion> builder)
    {
        builder.ToTable("kb_suggestions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Op).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ContentMd).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Rationale).HasColumnType("nvarchar(max)");
        builder.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.DedupHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReviewerVerdict).HasMaxLength(16);
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ApprovalMode).HasMaxLength(8);
        builder.Property(x => x.RejectedReason).HasMaxLength(1024);
        builder.Property(x => x.AccuracyBefore).HasPrecision(5, 2);
        builder.Property(x => x.AccuracyAfter).HasPrecision(5, 2);
        // Job đêm idempotent: chạy lại không nhân đôi đề xuất cùng câu-hỏi-chuẩn-hóa.
        builder.HasIndex(x => new { x.TenantId, x.DedupHash }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

public sealed class PancakeConfigConfiguration : IEntityTypeConfiguration<PancakeConfig>
{
    public void Configure(EntityTypeBuilder<PancakeConfig> builder)
    {
        builder.ToTable("pancake_configs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BaseUrl).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AccessTokenEncrypted).HasColumnName("access_token_encrypted").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.WebhookSecretEncrypted).HasColumnName("webhook_secret_encrypted").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.SignatureHeader).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SignatureAlgo).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SignatureEncoding).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SendPathTemplate).HasMaxLength(512).IsRequired();
        builder.Property(x => x.AuthMode).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => x.TenantId).IsUnique();
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
        builder.Property(x => x.LlmConfigId);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.AgentType });
        builder.HasIndex(x => x.LlmConfigId);
        // Optional FK with NO ACTION avoids SQL Server multiple-cascade-path failures.
        // Rebind agents before deleting a provider config. No navigation property — resolver loads by id.
        builder.HasOne<LlmConfig>()
            .WithMany()
            .HasForeignKey(x => x.LlmConfigId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AgentSessionConfiguration : IEntityTypeConfiguration<AgentSession>
{
    public void Configure(EntityTypeBuilder<AgentSession> builder)
    {
        builder.ToTable("agent_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequiresApproval).HasDefaultValue(false);
        builder.Property(x => x.ReplanCount).HasDefaultValue(0);
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.ArchivedAt).HasColumnName("archived_at");
        builder.HasMany(x => x.Traces).WithOne().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.StartedAt });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.StartedAt });
        builder.HasIndex(x => new { x.TenantId, x.ArchivedAt, x.StartedAt });
        // SPEC-16 P3-3: index for fetching a user's runs (notification targeting + run list by user).
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.StartedAt });
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

public sealed class AgentDefinitionConfiguration : IEntityTypeConfiguration<AgentDefinition>
{
    public void Configure(EntityTypeBuilder<AgentDefinition> builder)
    {
        builder.ToTable("agent_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AgentType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PersonaPrompt).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.AllowedToolsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.InputSchemaJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.OutputSchemaJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.MemoryScope).HasMaxLength(32).IsRequired();
        builder.Property(x => x.KbModuleCode).HasMaxLength(64);
        builder.Property(x => x.Version).HasDefaultValue(1);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsOrchestratable });
        builder.HasIndex(x => x.LlmConfigId);
        builder.HasOne<LlmConfig>().WithMany().HasForeignKey(x => x.LlmConfigId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AgentA2AMessageConfiguration : IEntityTypeConfiguration<AgentA2AMessage>
{
    public void Configure(EntityTypeBuilder<AgentA2AMessage> builder)
    {
        builder.ToTable("agent_a2a_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TaskId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Intent).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(1024);
        builder.HasIndex(x => new { x.TenantId, x.SessionId, x.Status, x.CreatedAt });
        builder.HasOne<AgentSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.FromAgentDefinitionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.ToAgentDefinitionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AgentScheduleConfiguration : IEntityTypeConfiguration<AgentSchedule>
{
    public void Configure(EntityTypeBuilder<AgentSchedule> builder)
    {
        builder.ToTable("agent_schedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.GoalTemplate).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Cadence).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CronExpression).HasMaxLength(128);
        builder.Property(x => x.TimezoneId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.OverlapPolicy).HasMaxLength(32).HasDefaultValue("skip").IsRequired();
        builder.Property(x => x.MisfirePolicy).HasMaxLength(32).HasDefaultValue("skip_missed").IsRequired();
        builder.Property(x => x.ApprovalPolicyJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.TriggerType).HasMaxLength(16).HasDefaultValue("cadence").IsRequired();
        builder.Property(x => x.EventKey).HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.IsActive, x.NextRunAt });
        builder.HasIndex(x => new { x.TenantId, x.Name });
    }
}

public sealed class AgentScheduleRunConfiguration : IEntityTypeConfiguration<AgentScheduleRun>
{
    public void Configure(EntityTypeBuilder<AgentScheduleRun> builder)
    {
        builder.ToTable("agent_schedule_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.WindowKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(1024);
        builder.HasIndex(x => new { x.ScheduleId, x.WindowKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.StartedAt });
        builder.HasOne<AgentSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AgentSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
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

public sealed class UpsellSuggestionCacheConfiguration : IEntityTypeConfiguration<UpsellSuggestionCache>
{
    public void Configure(EntityTypeBuilder<UpsellSuggestionCache> builder)
    {
        builder.ToTable("sale_assist_upsell_suggestions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Suggestion).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(400).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ConversationId }).IsUnique();
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
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ApprovedByAgentId).HasColumnName("approved_by_agent_id");
        builder.Property(x => x.RejectedReason).HasMaxLength(1024);
        builder.Property(x => x.ContentRevision).HasDefaultValue(1);
        builder.Property(x => x.AgentReviewStatus)
            .HasMaxLength(24)
            .HasDefaultValue(ContentItem.ReviewStatusPending)
            .IsRequired();
        builder.Property(x => x.AgentReviewReason).HasMaxLength(ContentItem.MaxReviewReasonLength);
        builder.Property(x => x.ImageReviewStatus)
            .HasMaxLength(24)
            .HasDefaultValue(ContentItem.ImageReviewStatusPending)
            .IsRequired();
        builder.Property(x => x.ReviewedImageCount).HasDefaultValue(0);
        builder.Property(x => x.AgentReviewAttemptCount).HasDefaultValue(0);
        builder.Property(x => x.PublishingPolicyApplied).HasMaxLength(32);
        builder.Property(x => x.HumanApprovalRequirementReason).HasMaxLength(32);
        builder.Property(x => x.ApprovalMode).HasMaxLength(16);
        builder.Property(x => x.ApprovalReason).HasMaxLength(ContentItem.MaxApprovalReasonLength);
        // Prompt chaining P4: ảnh chụp L1/L2 để repurpose tái dùng (§4.5). NVARCHAR(MAX) — plan+outline có thể dài.
        builder.Property(x => x.ChainPlanJson).HasColumnName("chain_plan_json");
        builder.Property(x => x.ChainOutlineJson).HasColumnName("chain_outline_json");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
    }
}

public sealed class ContentScheduleConfiguration : IEntityTypeConfiguration<ContentSchedule>
{
    public void Configure(EntityTypeBuilder<ContentSchedule> builder)
    {
        // SQL Server writer-gate trigger forbids OUTPUT without INTO; EF must not use OUTPUT clause.
        builder.ToTable("content_schedule", table =>
            table.HasTrigger("TR_content_schedule_writer_gate"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ContentRevision);
        builder.Property(x => x.ActiveRevisionSlot).HasColumnName("active_revision_slot");
        builder.Property(x => x.ApprovalMode).HasMaxLength(16);
        builder.Property(x => x.Status)
            .HasMaxLength(32)
            .HasDefaultValue(ContentSchedule.StatusPending)
            .IsRequired();
        builder.Property(x => x.PostUrl).HasMaxLength(512);
        builder.Property(x => x.ExternalPostId)
            .HasColumnName("external_post_id")
            .HasMaxLength(ContentSchedule.MaxExternalPostIdLength);
        builder.Property(x => x.ProviderTargetId).HasColumnName("provider_target_id").HasMaxLength(128);
        builder.Property(x => x.MetaCommentsSyncedAt).HasColumnName("meta_comments_synced_at");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(ContentSchedule.MaxLastErrorLength);
        builder.Property(x => x.LastErrorCode).HasMaxLength(128);
        builder.Property(x => x.RetryCount).HasColumnName("retry_count");
        builder.HasOne<MetaAsset>()
            .WithMany()
            .HasForeignKey(x => x.MetaAssetId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ContentItem>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ContentItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.MetaAssetId);
        builder.HasIndex(x => new { x.TenantId, x.ScheduledAt });
        builder.HasIndex(x => new
            {
                x.TenantId,
                x.ContentItemId,
                x.ActiveRevisionSlot,
            })
            .IsUnique()
            .HasFilter("[active_revision_slot] IS NOT NULL");
    }
}

public sealed class ContentReviewTaskConfiguration : IEntityTypeConfiguration<ContentReviewTask>
{
    public void Configure(EntityTypeBuilder<ContentReviewTask> builder)
    {
        builder.ToTable("content_review_tasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.LastErrorCode).HasMaxLength(128);
        // Refine P6 (§4.7): số vòng sửa-tự-động đã dùng cho revision này; đúng 1 vòng nên chỉ 0→1.
        builder.Property(x => x.RefineAttemptCount).HasDefaultValue(0);
        builder.HasOne<ContentItem>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ContentItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.TenantId, x.ContentItemId, x.ContentRevision }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.LeaseExpiresAt });
    }
}

public sealed class ContentRenderTaskConfiguration : IEntityTypeConfiguration<ContentRenderTask>
{
    public void Configure(EntityTypeBuilder<ContentRenderTask> builder)
    {
        builder.ToTable("content_render_tasks", table =>
        {
            table.HasCheckConstraint(
                "CK_content_render_tasks_revision",
                "source_revision > 0 AND source_revision < 2147483647 AND template_version > 0 AND attempt_count >= 0");
            table.HasCheckConstraint(
                "CK_content_render_tasks_status",
                "status IN ('pending', 'leased', 'completed', 'failed', 'canceled_stale')");
            table.HasCheckConstraint(
                "CK_content_render_tasks_preset",
                "preset IN ('1200x630', '1080x1080')");
            table.HasCheckConstraint(
                "CK_content_render_tasks_state",
                "(status = 'pending' AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NULL AND output_asset_id IS NULL AND completed_revision IS NULL) OR "
                + "(status = 'leased' AND lease_token IS NOT NULL AND (claimed_lease_token IS NULL OR claimed_lease_token = lease_token) AND lease_expires_at IS NOT NULL AND completed_at IS NULL AND output_asset_id IS NULL AND completed_revision IS NULL) OR "
                + "(status = 'completed' AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL AND output_asset_id IS NOT NULL AND completed_revision = source_revision + 1) OR "
                + "(status IN ('failed', 'canceled_stale') AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL AND output_asset_id IS NULL AND completed_revision IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TemplateId)
            .HasMaxLength(ContentRenderTask.MaxTemplateIdLength)
            .IsRequired();
        builder.Property(x => x.TemplateHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Preset)
            .HasMaxLength(ContentRenderTask.MaxPresetLength)
            .IsRequired();
        builder.Property(x => x.CanonicalSlotsJson)
            .HasMaxLength(ContentRenderTask.MaxCanonicalSlotsUtf8Bytes)
            .IsRequired();
        builder.Property(x => x.SlotsHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(24)
            .HasDefaultValue(ContentRenderTask.StatusPending)
            .IsRequired();
        builder.Property(x => x.AttemptCount).HasDefaultValue(0);
        builder.Property(x => x.LastErrorCode)
            .HasMaxLength(ContentRenderTask.MaxErrorCodeLength);
        builder.HasOne<ContentItem>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ContentItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.TenantId, x.ContentItemId, x.SourceRevision })
            .IsUnique()
            .HasDatabaseName("UX_content_render_tasks_item_revision");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAt, x.CreatedAt })
            .HasDatabaseName("IX_content_render_tasks_due");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.LeaseExpiresAt })
            .HasDatabaseName("IX_content_render_tasks_expired_lease");
    }
}

public sealed class ContentAssetConfiguration : IEntityTypeConfiguration<ContentAsset>
{
    public void Configure(EntityTypeBuilder<ContentAsset> builder)
    {
        builder.ToTable("content_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StorageKey).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.Sha256).HasColumnType("binary(32)");
        builder.Property(x => x.ContentType).HasMaxLength(128);
        builder.Property(x => x.OriginalFileName).HasMaxLength(255);
        builder.Property(x => x.LastErrorCode).HasMaxLength(128);
        builder.HasOne<ContentItem>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ContentItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ContentItemId, x.Status, x.SortOrder });
        builder.HasIndex(x => new { x.TenantId, x.ContentItemId, x.SortOrder })
            .IsUnique()
            .HasFilter("[status] = 'ready'");
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

public sealed class ContentPublishAttemptConfiguration : IEntityTypeConfiguration<ContentPublishAttempt>
{
    public void Configure(EntityTypeBuilder<ContentPublishAttempt> builder)
    {
        // SQL Server writer-gate trigger forbids OUTPUT without INTO; EF must not use OUTPUT clause.
        builder.ToTable("content_publish_attempts", table =>
            table.HasTrigger("TR_content_publish_attempts_writer_gate"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(x => x.BodySnapshot).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.AssetsSnapshotJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.SnapshotSha256).HasColumnType("binary(32)").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.ProviderRequestId).HasMaxLength(256);
        builder.Property(x => x.ExternalPostId).HasMaxLength(256);
        builder.Property(x => x.LastErrorCode).HasMaxLength(128);
        builder.HasOne<ContentSchedule>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ScheduleId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ContentItem>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ContentItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.AttemptToken).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        // One active claim only; failed/outcome_unknown/succeeded rows may coexist for history/retry.
        builder.HasIndex(x => new
            {
                x.TenantId,
                x.ScheduleId,
                x.ContentItemId,
                x.ContentRevision,
                x.PublishTargetId,
            })
            .IsUnique()
            .HasFilter("[status] IN ('claimed', 'transmitted')");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.ClaimedAt });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.LeaseExpiresAt });
    }
}

public sealed class ContentWorkflowMetricsHourlyConfiguration
    : IEntityTypeConfiguration<ContentWorkflowMetricsHourly>
{
    public void Configure(EntityTypeBuilder<ContentWorkflowMetricsHourly> builder)
    {
        builder.ToTable("content_workflow_metrics_hourly");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ReviewPassedCount).HasDefaultValue(0L);
        builder.Property(x => x.ReviewRejectedCount).HasDefaultValue(0L);
        builder.Property(x => x.ReviewNeedsHumanCount).HasDefaultValue(0L);
        builder.Property(x => x.ReviewFailedCount).HasDefaultValue(0L);
        builder.Property(x => x.ImageReviewedCount).HasDefaultValue(0L);
        builder.Property(x => x.ImageNotApplicableCount).HasDefaultValue(0L);
        builder.Property(x => x.ImageSkippedUnsupportedCount).HasDefaultValue(0L);
        builder.Property(x => x.ImageFailedCount).HasDefaultValue(0L);
        builder.Property(x => x.HumanFallbackCount).HasDefaultValue(0L);
        builder.Property(x => x.HumanOverrideCount).HasDefaultValue(0L);
        builder.Property(x => x.HumanRejectCount).HasDefaultValue(0L);
        builder.Property(x => x.HeldScheduleCount).HasDefaultValue(0L);
        builder.Property(x => x.PublishSucceededCount).HasDefaultValue(0L);
        builder.Property(x => x.PublishFailedCount).HasDefaultValue(0L);
        builder.Property(x => x.PublishOutcomeUnknownCount).HasDefaultValue(0L);
        builder.Property(x => x.ReviewLatencyMsSum).HasDefaultValue(0L);
        builder.Property(x => x.ReviewLatencySampleCount).HasDefaultValue(0L);
        builder.Property(x => x.PublishLatencyMsSum).HasDefaultValue(0L);
        builder.Property(x => x.PublishLatencySampleCount).HasDefaultValue(0L);
        builder.Property(x => x.LlmInputTokens).HasDefaultValue(0L);
        builder.Property(x => x.LlmOutputTokens).HasDefaultValue(0L);
        builder.Property(x => x.LlmCostUsd).HasPrecision(18, 6).HasDefaultValue(0m);
        builder.HasIndex(x => new { x.TenantId, x.HourUtc }).IsUnique();
        builder.HasIndex(x => x.HourUtc);
    }
}

public sealed class ContentGenerationTraceConfiguration
    : IEntityTypeConfiguration<ContentGenerationTrace>
{
    public void Configure(EntityTypeBuilder<ContentGenerationTrace> builder)
    {
        builder.ToTable("content_generation_traces");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.StepId).HasMaxLength(ContentGenerationTrace.StepIdMaxLength).IsRequired();
        builder.Property(x => x.PromptVersion).HasMaxLength(ContentGenerationTrace.PromptVersionMaxLength).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(ContentGenerationTrace.ModelMaxLength);
        builder.Property(x => x.GateResult).HasMaxLength(ContentGenerationTrace.GateResultMaxLength).IsRequired();
        builder.Property(x => x.PayloadJson).HasMaxLength(ContentGenerationTrace.PayloadJsonMaxLength);
        builder.Property(x => x.InputTokens).HasDefaultValue(0);
        builder.Property(x => x.OutputTokens).HasDefaultValue(0);
        builder.Property(x => x.UsdCost).HasPrecision(18, 6).HasDefaultValue(0m);
        builder.Property(x => x.LatencyMs).HasDefaultValue(0L);
        // Truy vấn theo tenant + thời gian (retention 30 ngày, xem trace gần nhất); nhóm theo lượt chạy chuỗi.
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => x.ChainRunId);
    }
}

public sealed class SocialCredentialConfiguration : IEntityTypeConfiguration<SocialCredential>
{
    public void Configure(EntityTypeBuilder<SocialCredential> builder)
    {
        builder.ToTable("social_credentials");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PageId).HasMaxLength(128);
        builder.Property(x => x.CredentialsEncrypted).HasColumnName("credentials_encrypted").IsRequired();
        // One active credential per (tenant, provider, page_id). Page_id included so a tenant can hold per-page FB tokens.
        builder.HasIndex(x => new { x.TenantId, x.Provider, x.PageId }).IsUnique();
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class MetaConnectionConfiguration : IEntityTypeConfiguration<MetaConnection>
{
    public void Configure(EntityTypeBuilder<MetaConnection> builder)
    {
        builder.ToTable("meta_connections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ClientBusinessId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SystemUserId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TokenType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AccessTokenEncrypted).HasColumnName("access_token_encrypted").IsRequired();
        builder.Property(x => x.GrantedScopesJson).HasColumnName("granted_scopes_json").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1024);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.TenantId).IsUnique();
    }
}

public sealed class MetaAssetConfiguration : IEntityTypeConfiguration<MetaAsset>
{
    public void Configure(EntityTypeBuilder<MetaAsset> builder)
    {
        builder.ToTable("meta_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AssetType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TasksJson).HasColumnName("tasks_json").IsRequired();
        builder.Property(x => x.AccessTokenEncrypted).HasColumnName("access_token_encrypted").IsRequired();
        builder.Property(x => x.FeedSubscribedAt).HasColumnName("feed_subscribed_at");
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MetaConnection>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ConnectionId);
        builder.HasIndex(x => new { x.TenantId, x.AssetType, x.ExternalId }).IsUnique();
    }
}

public sealed class MetaOAuthStateConfiguration : IEntityTypeConfiguration<MetaOAuthState>
{
    public void Configure(EntityTypeBuilder<MetaOAuthState> builder)
    {
        builder.ToTable("meta_oauth_states");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StateHash).HasMaxLength(64).IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.StateHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
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

public sealed class AdsCreativeConfiguration : IEntityTypeConfiguration<AdsCreative>
{
    public void Configure(EntityTypeBuilder<AdsCreative> builder)
    {
        builder.ToTable("ads_creatives");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalCreativeId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.CampaignId, x.Status });
    }
}

public sealed class AdsMetricsDailyConfiguration : IEntityTypeConfiguration<AdsMetricsDaily>
{
    public void Configure(EntityTypeBuilder<AdsMetricsDaily> builder)
    {
        builder.ToTable("ads_metrics_daily");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CampaignId, x.MetricDate }).IsUnique();
    }
}

public sealed class KpiDailyConfiguration : IEntityTypeConfiguration<KpiDaily>
{
    public void Configure(EntityTypeBuilder<KpiDaily> builder)
    {
        builder.ToTable("kpi_daily");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AdSpend).HasPrecision(18, 2);
        builder.Property(x => x.Revenue).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.TenantId, x.Date, x.Platform }).IsUnique();
    }
}

public sealed class ExperimentConfiguration : IEntityTypeConfiguration<Experiment>
{
    public void Configure(EntityTypeBuilder<Experiment> builder)
    {
        builder.ToTable("experiments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasMany(x => x.Variants).WithOne().HasForeignKey(x => x.ExperimentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.TargetType, x.TargetId, x.Status });
    }
}

public sealed class ExperimentVariantConfiguration : IEntityTypeConfiguration<ExperimentVariant>
{
    public void Configure(EntityTypeBuilder<ExperimentVariant> builder)
    {
        builder.ToTable("experiment_variants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.HasOne<Experiment>().WithMany(x => x.Variants).HasForeignKey(x => x.ExperimentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatScenario>().WithMany().HasForeignKey(x => x.ChatScenarioId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<KbVersion>().WithMany().HasForeignKey(x => x.KbVersionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.ExperimentId, x.Code }).IsUnique();
    }
}

public sealed class ExperimentAssignmentConfiguration : IEntityTypeConfiguration<ExperimentAssignment>
{
    public void Configure(EntityTypeBuilder<ExperimentAssignment> builder)
    {
        builder.ToTable("experiment_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectKey).HasMaxLength(256).IsRequired();
        builder.HasOne<Experiment>().WithMany().HasForeignKey(x => x.ExperimentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExperimentVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.TenantId, x.ExperimentId, x.SubjectKey }).IsUnique();
        builder.HasIndex(x => new { x.ExperimentId, x.VariantId });
    }
}

public sealed class ExperimentEventConfiguration : IEntityTypeConfiguration<ExperimentEvent>
{
    public void Configure(EntityTypeBuilder<ExperimentEvent> builder)
    {
        builder.ToTable("experiment_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectKey).HasMaxLength(256).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(32).IsRequired();
        builder.HasOne<Experiment>().WithMany().HasForeignKey(x => x.ExperimentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExperimentVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.TenantId, x.ExperimentId, x.EventType, x.OccurredAt });
        builder.HasIndex(x => new { x.ExperimentId, x.VariantId, x.SubjectKey });
    }
}

public sealed class LlmConfigConfiguration : IEntityTypeConfiguration<LlmConfig>
{
    public void Configure(EntityTypeBuilder<LlmConfig> builder)
    {
        builder.ToTable("llm_configs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        builder.Property(x => x.ApiKeyEncrypted).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(512);
        // Numeric-suffixed names snake-case ambiguously; pin them so DDL + EF agree.
        builder.Property(x => x.InputUsdPer1M).HasColumnName("input_usd_per_1m").HasColumnType("decimal(10,4)");
        builder.Property(x => x.OutputUsdPer1M).HasColumnName("output_usd_per_1m").HasColumnType("decimal(10,4)");
        builder.Property(x => x.TimeoutSeconds).HasColumnName("timeout_seconds");
        builder.Property(x => x.MaxOutputTokens).HasColumnName("max_output_tokens");
        builder.Property(x => x.SupportsVision).HasColumnName("supports_vision");
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

public sealed class EmbeddingConfigConfiguration : IEntityTypeConfiguration<EmbeddingConfig>
{
    public void Configure(EntityTypeBuilder<EmbeddingConfig> builder)
    {
        builder.ToTable("embedding_configs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        builder.Property(x => x.ApiKeyEncrypted).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(512);
        builder.Property(x => x.Dimension).HasDefaultValue(1536);
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

public sealed class KpiForecastConfiguration : IEntityTypeConfiguration<KpiForecast>
{
    public void Configure(EntityTypeBuilder<KpiForecast> builder)
    {
        builder.ToTable("kpi_forecast");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Metric).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Platform, x.Metric, x.ForecastDate }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Metric, x.ForecastDate });
    }
}

public sealed class InboxConfiguration : IEntityTypeConfiguration<Inbox>
{
    public void Configure(EntityTypeBuilder<Inbox> builder)
    {
        builder.ToTable("inboxes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalPageId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AvatarUrl).HasMaxLength(512);
        builder.Property(x => x.EncryptedAccessToken).HasColumnName("encrypted_access_token");
        builder.Property(x => x.EncryptedRefreshToken).HasColumnName("encrypted_refresh_token");
        builder.Property(x => x.EncryptedWebhookSecret).HasColumnName("encrypted_webhook_secret");
        builder.Property(x => x.TokenExpiresAt).HasColumnName("token_expires_at");
        builder.Property(x => x.PageTokenMintedAt).HasColumnName("page_token_minted_at");
        builder.HasIndex(x => new { x.TenantId, x.Platform, x.ExternalPageId })
            .IsUnique()
            .HasFilter("[is_active] = 1 AND [deleted_at] IS NULL");
    }
}

public sealed class InboxMemberConfiguration : IEntityTypeConfiguration<InboxMember>
{
    public void Configure(EntityTypeBuilder<InboxMember> builder)
    {
        builder.ToTable("inbox_members");
        builder.HasKey(x => new { x.InboxId, x.AgentId });
        builder.HasOne<Inbox>().WithMany().HasForeignKey(x => x.InboxId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("labels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(32).IsRequired();
    }
}

public sealed class ConversationLabelConfiguration : IEntityTypeConfiguration<ConversationLabel>
{
    public void Configure(EntityTypeBuilder<ConversationLabel> builder)
    {
        builder.ToTable("conversation_labels");
        builder.HasKey(x => new { x.ConversationId, x.LabelId });
    }
}

public sealed class ConversationNoteConfiguration : IEntityTypeConfiguration<ConversationNote>
{
    public void Configure(EntityTypeBuilder<ConversationNote> builder)
    {
        builder.ToTable("conversation_notes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedByDisplayName).HasMaxLength(256);
    }
}
