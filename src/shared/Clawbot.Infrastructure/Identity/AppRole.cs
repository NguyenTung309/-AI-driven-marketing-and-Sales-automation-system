using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Identity;

public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole() { }

    public AppRole(string name) : base(name) { }
}
