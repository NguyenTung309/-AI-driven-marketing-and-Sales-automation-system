using Clawbot.SharedKernel.Time;

namespace Clawbot.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
