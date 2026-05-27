namespace Clawbot.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
