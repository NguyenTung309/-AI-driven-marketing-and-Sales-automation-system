using Clawbot.Domain.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class RequestStatsHourlyConfiguration : IEntityTypeConfiguration<RequestStatsHourly>
{
    public void Configure(EntityTypeBuilder<RequestStatsHourly> builder)
    {
        builder.ToTable("request_stats_hourly");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.BucketHour).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.StatusClass).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Count).IsRequired();
        builder.HasIndex(x => new { x.BucketHour, x.TenantId, x.StatusClass }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BucketHour });
    }
}
