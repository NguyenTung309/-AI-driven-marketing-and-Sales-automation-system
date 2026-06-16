using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    // SPEC-11 D11: mirrors the is_active flag so login + refresh can deny disabled
    // accounts (not just lockout). Maps to AspNetUsers.is_active via snake-case naming.
    // M23 profile + admin fields.
    public bool IsActive { get; set; } = true;
    public DateOnly? DateOfBirth { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
