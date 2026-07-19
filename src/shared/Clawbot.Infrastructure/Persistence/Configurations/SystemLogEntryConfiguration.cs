using Clawbot.Domain.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class SystemLogEntryConfiguration : IEntityTypeConfiguration<SystemLogEntry>
{
    public void Configure(EntityTypeBuilder<SystemLogEntry> builder)
    {
        builder.ToTable("system_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Level).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(256);
        builder.Property(x => x.Message).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.Exception);
        builder.Property(x => x.StatusCode);
        builder.Property(x => x.Method).HasMaxLength(10);
        builder.Property(x => x.Path).HasMaxLength(512);
        builder.Property(x => x.ElapsedMs);
        builder.Property(x => x.TraceId).HasMaxLength(64);
        builder.Property(x => x.TenantId);
        builder.Property(x => x.UserId);
        builder.Property(x => x.Properties);
        builder.HasIndex(x => x.OccurredAt).IsDescending();
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
