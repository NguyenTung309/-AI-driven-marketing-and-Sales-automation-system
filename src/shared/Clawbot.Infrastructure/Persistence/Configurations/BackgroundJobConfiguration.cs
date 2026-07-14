using Clawbot.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class BackgroundJobConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.ToTable("background_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProgressNote).HasMaxLength(200);
        builder.Property(x => x.ResultLink).HasMaxLength(400);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.Property(x => x.HangfireJobId).HasMaxLength(64);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
        // KHÔNG unique: cùng idempotency key được chạy lại sau khi job trước đã kết thúc
        // (chỉ job đang chạy/chờ mới được tái dùng — xem HangfireJobLauncher).
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey, x.Status })
            .HasFilter("[idempotency_key] IS NOT NULL");
    }
}
