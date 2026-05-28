using Clawbot.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalThreadId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.Platform, x.ExternalThreadId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.LastMessageAt });
    }
}
