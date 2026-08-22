using Clawbot.Domain.Security;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Security;

public sealed class PermissionTests
{
    [Fact]
    public void Create_SetsCodeAndDescription()
    {
        var perm = Permission.Create("leads:read", "Read leads");

        perm.Code.Should().Be("leads:read");
        perm.Description.Should().Be("Read leads");
        perm.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_NullDescription_Allowed()
    {
        var perm = Permission.Create("admin:all");

        perm.Description.Should().BeNull();
    }
}
