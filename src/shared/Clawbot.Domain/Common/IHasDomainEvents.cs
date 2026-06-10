namespace Clawbot.Domain.Common;

// Non-generic accessor so infrastructure (SaveChanges interceptor) can collect domain
// events off any AggregateRoot&lt;TId&gt; without knowing the key type.
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
