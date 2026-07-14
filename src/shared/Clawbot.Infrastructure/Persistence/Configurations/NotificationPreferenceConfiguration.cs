using Clawbot.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.Type }).IsUnique();
    }
}

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Endpoint).HasMaxLength(512).IsRequired();
        builder.Property(x => x.P256dh).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Auth).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.Endpoint).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.UserId });
    }
}
