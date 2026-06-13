using Clawbot.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Link).HasMaxLength(256);
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.IsRead, x.CreatedAt });
    }
}
