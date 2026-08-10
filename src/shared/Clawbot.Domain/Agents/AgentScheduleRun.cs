using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentScheduleRun : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ScheduleId { get; private set; }
    public Guid? SessionId { get; private set; }
    // The actor whose current permissions authorized this individual run.
    public Guid? InitiatorUserId { get; private set; }
    public string WindowKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = "started";
    public string? Error { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? LastHeartbeatAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private AgentScheduleRun() { }

    public static AgentScheduleRun Start(
        Guid tenantId,
        Guid scheduleId,
        string windowKey,
        DateTimeOffset startedAt,
        Guid? initiatorUserId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScheduleId = scheduleId,
            InitiatorUserId = initiatorUserId,
            WindowKey = windowKey.Trim(),
            StartedAt = startedAt,
            LastHeartbeatAt = startedAt,
        };

    public void LinkSession(Guid sessionId) => SessionId = sessionId;

    public void SetInitiator(Guid? initiatorUserId) => InitiatorUserId = initiatorUserId;

    public void Heartbeat(DateTimeOffset at)
    {
        if (Status == "started" && FinishedAt is null)
            LastHeartbeatAt = at;
    }

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
