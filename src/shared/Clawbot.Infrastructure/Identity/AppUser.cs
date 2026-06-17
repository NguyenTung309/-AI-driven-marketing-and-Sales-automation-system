using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    // M23 profile + admin fields (map to existing/added columns on the `users` table).
    public DateOnly? DateOfBirth { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}
