namespace Clawbot.SharedKernel.Demo;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Mode { get; init; }
    public bool MaskPii { get; init; } = true;
    public bool SkipHmac { get; init; }
    public string? AdminKey { get; init; }
    public int SseReplayCount { get; init; } = 10;
    public int WatchdogIntervalSeconds { get; init; } = 60;
    public int TraceTtlMinutes { get; init; } = 60;

    public int EffectiveTtlMinutes => Math.Clamp(TraceTtlMinutes, 5, 1440);
    public TimeSpan WatchdogInterval => TimeSpan.FromSeconds(Math.Max(WatchdogIntervalSeconds, 10));
}
