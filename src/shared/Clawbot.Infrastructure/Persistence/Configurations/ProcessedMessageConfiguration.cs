using Clawbot.Domain.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExternalMessageId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ConversationExternalId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProcessedAt).IsRequired();
        builder.HasIndex(x => new { x.Platform, x.ExternalMessageId }).IsUnique();
    }
}
