using Clawbot.Domain.Common;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Common;

public sealed class EntityEqualityTests
{
    private sealed class SampleEntity : Entity<Guid>
    {
        public SampleEntity(Guid id) => Id = id;
    }

    private sealed class OtherEntity : Entity<Guid>
    {
        public OtherEntity(Guid id) => Id = id;
    }

    [Fact]
    public void Equals_SameIdSameType_IsTrue()
    {
        var id = Guid.NewGuid();

        new SampleEntity(id).Equals(new SampleEntity(id)).Should().BeTrue();
    }

    [Fact]
    public void Equals_SameReference_IsTrue()
    {
        var entity = new SampleEntity(Guid.NewGuid());

        entity.Equals(entity).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentId_IsFalse()
    {
        new SampleEntity(Guid.NewGuid()).Equals(new SampleEntity(Guid.NewGuid()))
            .Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentConcreteType_IsFalse()
    {
        // Cùng Id nhưng khác loại entity thì không được coi là một.
        var id = Guid.NewGuid();

        new SampleEntity(id).Equals(new OtherEntity(id)).Should().BeFalse();
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        new SampleEntity(Guid.NewGuid()).Equals(null).Should().BeFalse();
    }

    [Fact]
    public void EqualsObject_NonEntity_IsFalse()
    {
        new SampleEntity(Guid.NewGuid()).Equals((object)"not-an-entity").Should().BeFalse();
    }

    [Fact]
    public void EqualsObject_MatchingEntity_IsTrue()
    {
        var id = Guid.NewGuid();

        new SampleEntity(id).Equals((object)new SampleEntity(id)).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameIdSameType_Matches()
    {
        var id = Guid.NewGuid();

        new SampleEntity(id).GetHashCode().Should().Be(new SampleEntity(id).GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentType_Differs()
    {
        var id = Guid.NewGuid();

        new SampleEntity(id).GetHashCode().Should().NotBe(new OtherEntity(id).GetHashCode());
    }

    [Fact]
    public void EqualityOperator_ComparesByValue()
    {
        var id = Guid.NewGuid();
        var left = new SampleEntity(id);
        var right = new SampleEntity(id);

        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void EqualityOperator_BothNull_IsTrue()
    {
        SampleEntity? left = null;
        SampleEntity? right = null;

        (left == right).Should().BeTrue();
    }

    [Fact]
    public void EqualityOperator_OneNull_IsFalse()
    {
        var entity = new SampleEntity(Guid.NewGuid());

        (entity == null).Should().BeFalse();
        (null == entity).Should().BeFalse();
        (entity != null).Should().BeTrue();
    }
}

public sealed class AggregateRootDomainEventTests
{
    private sealed record SampleEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class SampleAggregate : AggregateRoot<Guid>
    {
        public SampleAggregate() => Id = Guid.NewGuid();

        public void Emit(IDomainEvent domainEvent) => Raise(domainEvent);
    }

    [Fact]
    public void DomainEvents_StartsEmpty()
    {
        new SampleAggregate().DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Raise_AppendsInOrder()
    {
        var aggregate = new SampleAggregate();
        var first = new SampleEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var second = new SampleEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch);

        aggregate.Emit(first);
        aggregate.Emit(second);

        aggregate.DomainEvents.Should().Equal(first, second);
    }

    [Fact]
    public void ClearDomainEvents_EmptiesCollection()
    {
        var aggregate = new SampleAggregate();
        aggregate.Emit(new SampleEvent(Guid.NewGuid(), DateTimeOffset.UnixEpoch));

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DomainEvents_ExposesReadOnlyView()
    {
        var aggregate = new SampleAggregate();

        aggregate.DomainEvents.Should().BeAssignableTo<IReadOnlyCollection<IDomainEvent>>();
    }
}
