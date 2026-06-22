using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentSession : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<AgentTrace> _traces = new();

    public Guid TenantId { get; private set; }
    public Guid? AgentId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public string? Goal { get; private set; }
    public string Status { get; private set; } = AgentSessionStatuses.Draft;
    public string PlanJson { get; private set; } = "{}";
    public bool RequiresApproval { get; private set; }
    public int ReplanCount { get; private set; }
    public byte[]? RowVersion { get; private set; }
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
            Status = AgentSessionStatuses.Running,
            StartedAt = startedAt,
        };

    public static AgentSession CreatePlan(
        Guid tenantId,
        string goal,
        string planJson,
        bool requiresApproval,
        DateTimeOffset startedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Goal = goal,
            PlanJson = string.IsNullOrWhiteSpace(planJson) ? "{}" : planJson,
            RequiresApproval = requiresApproval,
            Status = requiresApproval ? AgentSessionStatuses.PendingApproval : AgentSessionStatuses.Running,
            StartedAt = startedAt,
        };

    public AgentTrace AppendTrace(string taskId, string agentName, string phase, string message, DateTimeOffset occurredAt)
    {
        var t = AgentTrace.Create(Id, taskId, agentName, phase, message, occurredAt);
        _traces.Add(t);
        return t;
    }

    public void Approve()
    {
        if (Status != AgentSessionStatuses.PendingApproval)
            throw new InvalidOperationException("Only pending orchestration plans can be approved.");

        Status = AgentSessionStatuses.Running;
    }

    public void UpdatePlan(string planJson)
    {
        if (Status is not (AgentSessionStatuses.Draft or AgentSessionStatuses.PendingApproval))
            throw new InvalidOperationException("Only draft or pending orchestration plans can be edited.");

        PlanJson = string.IsNullOrWhiteSpace(planJson) ? "{}" : planJson;
    }

    public void Pause()
    {
        if (Status != AgentSessionStatuses.Running)
            throw new InvalidOperationException("Only running orchestration plans can be paused.");

        Status = AgentSessionStatuses.Paused;
    }

    public void Resume()
    {
        if (Status != AgentSessionStatuses.Paused)
            throw new InvalidOperationException("Only paused orchestration plans can be resumed.");

        Status = AgentSessionStatuses.Running;
    }

    public void IncrementReplan() => ReplanCount++;

    /// <summary>Persist the executed plan (with per-task statuses/outputs) after a run. No state guard.</summary>
    public void RecordRun(string planJson) =>
        PlanJson = string.IsNullOrWhiteSpace(planJson) ? "{}" : planJson;

    public void Cancel(DateTimeOffset at)
    {
        if (Status is not (AgentSessionStatuses.Running or AgentSessionStatuses.Paused))
            throw new InvalidOperationException("Only running or paused orchestration plans can be cancelled.");

        Status = AgentSessionStatuses.Cancelled;
        FinishedAt = at;
    }

    public void Finish(DateTimeOffset at)
    {
        Status = AgentSessionStatuses.Completed;
        FinishedAt = at;
    }

    public void Fail(DateTimeOffset at)
    {
        Status = AgentSessionStatuses.Failed;
        FinishedAt = at;
    }
}
