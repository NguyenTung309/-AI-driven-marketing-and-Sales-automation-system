using Clawbot.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.ExpiresAt });
        builder.HasIndex(x => x.TokenHash);
        builder.HasIndex(x => x.FamilyId);
        // No FK navigation to AspNetUsers — see RefreshToken remarks.
    }
}
