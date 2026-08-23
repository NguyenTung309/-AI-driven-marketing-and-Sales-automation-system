using Clawbot.Domain.Security;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Security;

public sealed class RolePermissionTests
{
    [Fact]
    public void Create_SetsCompositeKey()
    {
        var roleId = Guid.NewGuid();
        var permId = Guid.NewGuid();

        var rp = RolePermission.Create(roleId, permId);

        rp.RoleId.Should().Be(roleId);
        rp.PermissionId.Should().Be(permId);
    }
}
