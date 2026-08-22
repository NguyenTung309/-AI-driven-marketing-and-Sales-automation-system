using Clawbot.Domain.Security;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Security;

public sealed class RoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var role = Role.Create(TenantId, "Sale", "Sales team", false, Now);

        role.TenantId.Should().Be(TenantId);
        role.Name.Should().Be("Sale");
        role.Description.Should().Be("Sales team");
        role.IsSystem.Should().BeFalse();
        role.CreatedAt.Should().Be(Now);
        role.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Seed_UsesFixedIdAndSetsIsSystem()
    {
        var fixedId = Guid.NewGuid();

        var role = Role.Seed(fixedId, TenantId, "Admin", Now);

        role.Id.Should().Be(fixedId);
        role.IsSystem.Should().BeTrue();
        role.Description.Should().BeNull();
    }
}
