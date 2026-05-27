namespace Clawbot.SharedKernel.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
