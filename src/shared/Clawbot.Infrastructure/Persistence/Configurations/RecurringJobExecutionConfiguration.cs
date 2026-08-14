using Clawbot.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class RecurringJobExecutionConfiguration : IEntityTypeConfiguration<RecurringJobExecution>
{
    public void Configure(EntityTypeBuilder<RecurringJobExecution> builder)
    {
        builder.ToTable("recurring_job_executions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DefinitionId).HasMaxLength(RecurringJobExecution.MaxDefinitionIdLength).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.RequestKey).HasMaxLength(RecurringJobExecution.MaxRequestKeyLength);
        builder.Property(x => x.HangfireBackgroundJobId).HasMaxLength(RecurringJobExecution.MaxHangfireJobIdLength);
        builder.Property(x => x.EnqueueClaimToken);
        builder.Property(x => x.EnqueueClaimedAt);
        builder.Property(x => x.ProgressNote).HasMaxLength(RecurringJobExecution.MaxProgressNoteLength);
        builder.Property(x => x.ResultSummary).HasMaxLength(RecurringJobExecution.MaxResultSummaryLength);
        builder.Property(x => x.ResultLink).HasMaxLength(RecurringJobExecution.MaxResultLinkLength);
        builder.Property(x => x.Error).HasMaxLength(RecurringJobExecution.MaxErrorLength);
        builder.HasIndex(x => new { x.DefinitionId, x.RequestedAt });
        builder.HasIndex(x => new { x.Status, x.RequestedAt });
        builder.HasIndex(x => x.RetryOfExecutionId);
        builder.HasIndex(x => new { x.DefinitionId, x.HangfireBackgroundJobId })
            .IsUnique()
            .HasFilter("[hangfire_background_job_id] IS NOT NULL");
        builder.HasIndex(x => new { x.RequestedTenantId, x.RequestedByUserId, x.RequestKey })
            .IsUnique()
            .HasFilter("[request_key] IS NOT NULL");
        builder.HasOne<RecurringJobExecution>()
            .WithMany()
            .HasForeignKey(x => x.RetryOfExecutionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RecurringJobExecutionAttemptConfiguration : IEntityTypeConfiguration<RecurringJobExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<RecurringJobExecutionAttempt> builder)
    {
        builder.ToTable("recurring_job_execution_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.HangfireBackgroundJobId)
            .HasMaxLength(RecurringJobExecutionAttempt.MaxHangfireJobIdLength)
            .IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.Error).HasMaxLength(RecurringJobExecutionAttempt.MaxErrorLength);
        builder.Property(x => x.WorkerId).HasMaxLength(RecurringJobExecutionAttempt.MaxWorkerIdLength);
        builder.HasIndex(x => new { x.ExecutionId, x.AttemptNumber }).IsUnique();
        builder.HasIndex(x => x.HangfireBackgroundJobId);
        builder.HasIndex(x => x.ExecutionId)
            .IsUnique()
            .HasFilter("[status] = 'running'");
        builder.HasOne<RecurringJobExecution>()
            .WithMany()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
