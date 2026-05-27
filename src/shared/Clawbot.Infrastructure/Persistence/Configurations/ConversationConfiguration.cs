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
        builder.Property(x => x.Channel).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalThreadId).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Channel, x.ExternalThreadId }).IsUnique();
    }
}
