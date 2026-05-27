using Clawbot.Domain.Common;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests;

#pragma warning disable CA1707 // Identifiers should not contain underscores
public sealed class EntityEqualityTests
{
    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id) => Id = id;
    }

    [Fact]
    public void Entities_with_same_id_are_equal()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new TestEntity(id);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Entities_with_different_ids_are_not_equal()
    {
        var a = new TestEntity(Guid.NewGuid());
        var b = new TestEntity(Guid.NewGuid());

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }
}
#pragma warning restore CA1707
