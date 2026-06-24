using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentScheduleRun : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ScheduleId { get; private set; }
    public Guid? SessionId { get; private set; }
    public string WindowKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = "started";
    public string? Error { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private AgentScheduleRun() { }

    public static AgentScheduleRun Start(Guid tenantId, Guid scheduleId, string windowKey, DateTimeOffset startedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScheduleId = scheduleId,
            WindowKey = windowKey.Trim(),
            StartedAt = startedAt,
        };

    public void LinkSession(Guid sessionId) => SessionId = sessionId;

    public void Complete(DateTimeOffset at)
    {
        Status = "completed";
        FinishedAt = at;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset at)
    {
        Status = "failed";
        FinishedAt = at;
        Error = error.Trim();
    }

    public void Cancel(DateTimeOffset at)
    {
        Status = "cancelled";
        FinishedAt = at;
    }

    public void SkipOverlap(DateTimeOffset at)
    {
        Status = "skipped_overlap";
        FinishedAt = at;
    }
}
