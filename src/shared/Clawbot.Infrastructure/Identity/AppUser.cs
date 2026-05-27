using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
