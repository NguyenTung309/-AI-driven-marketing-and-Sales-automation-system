using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentSession : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<AgentTrace> _traces = new();

    public Guid TenantId { get; private set; }
    public Guid? AgentId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public string? Goal { get; private set; }
    public string Status { get; private set; } = "pending";
    public string PlanJson { get; private set; } = "{}";
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public IReadOnlyCollection<AgentTrace> Traces => _traces.AsReadOnly();

    private AgentSession() { }

    public static AgentSession Start(Guid tenantId, Guid? agentId, Guid? conversationId, string goal, DateTimeOffset startedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentId = agentId,
            ConversationId = conversationId,
            Goal = goal,
            Status = "running",
            StartedAt = startedAt,
        };

    public AgentTrace AppendTrace(string taskId, string agentName, string phase, string message, DateTimeOffset occurredAt)
    {
        var t = AgentTrace.Create(Id, taskId, agentName, phase, message, occurredAt);
        _traces.Add(t);
        return t;
    }

    public void Finish(DateTimeOffset at)
    {
        Status = "completed";
        FinishedAt = at;
    }
}
