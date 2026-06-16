using Clawbot.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class ClaudeCostEntryConfiguration : IEntityTypeConfiguration<ClaudeCostEntry>
{
    public void Configure(EntityTypeBuilder<ClaudeCostEntry> builder)
    {
        builder.ToTable("claude_cost_ledger");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Usd).HasColumnType("decimal(12,6)");
        builder.HasIndex(x => new { x.TenantId, x.AgentCode, x.CreatedAt });
    }
}
