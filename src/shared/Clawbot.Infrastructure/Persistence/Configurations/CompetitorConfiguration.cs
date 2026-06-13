using Clawbot.Domain.Competitors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class CompetitorSourceConfiguration : IEntityTypeConfiguration<CompetitorSource>
{
    public void Configure(EntityTypeBuilder<CompetitorSource> builder)
    {
        builder.ToTable("competitor_sources");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.SourceType).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

public sealed class CompetitorPostConfiguration : IEntityTypeConfiguration<CompetitorPost>
{
    public void Configure(EntityTypeBuilder<CompetitorPost> builder)
    {
        builder.ToTable("competitor_posts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Url).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Snippet).HasMaxLength(1024);
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.DetectedAt });
        builder.HasIndex(x => new { x.SourceId, x.ContentHash }).IsUnique();
    }
}
