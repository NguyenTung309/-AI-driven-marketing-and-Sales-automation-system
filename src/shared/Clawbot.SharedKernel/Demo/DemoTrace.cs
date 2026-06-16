namespace Clawbot.SharedKernel.Demo;

public sealed record DemoTrace
{
    public required string TraceId { get; init; }
    public DemoTraceStatus Status { get; set; } = DemoTraceStatus.Pending;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public long? TotalDurationMs { get; set; }
    public List<DemoTraceStep> Steps { get; init; } = [];
    public List<string> Errors { get; init; } = [];

    public void AddStep(DemoTraceStep step)
    {
        step.TimestampUtc ??= DateTime.UtcNow;
        Steps.Add(step);
    }
}

public sealed record DemoTraceStep
{
    public required string Layer { get; init; }
    public DemoTraceStepStatus Status { get; set; }
    public long? DurationMs { get; set; }
    public DateTime? TimestampUtc { get; set; }
    public string? Reason { get; set; }
    public string? LinkedTraceId { get; set; }
    public Dictionary<string, object?> Output { get; init; } = [];
}

public enum DemoTraceStatus
{
    Pending,
    Running,
    Completed,
    Partial
}

public enum DemoTraceStepStatus
{
    Pending,
    Running,
    Success,
    Failed,
    Skipped
}
