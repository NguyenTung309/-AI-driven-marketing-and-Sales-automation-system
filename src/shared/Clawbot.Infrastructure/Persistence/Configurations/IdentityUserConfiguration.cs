using Clawbot.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

// M23 Identity↔DDL reconcile (Option A): EF maps AppUser to the `users` table that the whole
// domain FKs to (api_keys, audit_logs, leads, messages, documents, content…). Without this,
// EF used the default `AspNetUsers` table which the DDL never creates → prod auth broken.
public sealed class IdentityUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");
        builder.Property(x => x.DisplayName).HasMaxLength(256);
        builder.Property(x => x.AvatarUrl).HasMaxLength(512);
    }
}
