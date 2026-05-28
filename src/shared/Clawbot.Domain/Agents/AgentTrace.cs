using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentTrace : Entity<Guid>
{
    public Guid SessionId { get; private set; }
    public string? TaskId { get; private set; }
    public string? AgentName { get; private set; }
    public string? Phase { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AgentTrace() { }

    internal static AgentTrace Create(Guid sessionId, string taskId, string agentName, string phase, string message, DateTimeOffset occurredAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TaskId = taskId,
            AgentName = agentName,
            Phase = phase,
            Message = message,
            OccurredAt = occurredAt,
        };
}
