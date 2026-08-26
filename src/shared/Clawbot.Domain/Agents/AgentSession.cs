using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentSession : AggregateRoot<Guid>, ITenantOwned
{
    public const int MaxPendingTerminalReasonLength = 1024;

    private readonly List<AgentTrace> _traces = new();

    public Guid TenantId { get; private set; }
    public Guid? AgentId { get; private set; }
    public Guid? ConversationId { get; private set; }
    // SPEC-16 P3-3: the user who initiated the orchestration run, so terminal notifications can be targeted to them.
    public Guid? UserId { get; private set; }
    // The current principal whose live permissions authorize execution. It can differ from UserId when
    // an authorized editor, approver, or resumer takes responsibility for a persisted plan.
    public Guid? ExecutionUserId { get; private set; }
    public string? Goal { get; private set; }
    public string Status { get; private set; } = AgentSessionStatuses.Draft;
    public string PlanJson { get; private set; } = "{}";
    public bool RequiresApproval { get; private set; }
    // Also serves as the durable orchestration plan generation: initial plan is generation 0.
    public int ReplanCount { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public byte[]? RowVersion { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public int? PendingTerminalGeneration { get; private set; }
    public DateTimeOffset? PendingTerminalRequestedAt { get; private set; }
    public string? PendingTerminalReason { get; private set; }

    public IReadOnlyCollection<AgentTrace> Traces => _traces.AsReadOnly();

    private AgentSession() { }

    public static AgentSession Start(Guid tenantId, Guid? agentId, Guid? conversationId, string goal, DateTimeOffset startedAt, Guid? userId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentId = agentId,
            ConversationId = conversationId,
            UserId = userId,
            ExecutionUserId = userId,
            Goal = goal,
            Status = AgentSessionStatuses.Running,
            StartedAt = startedAt,
        };

    public static AgentSession CreatePlan(
        Guid tenantId,
        string goal,
        string planJson,
        bool requiresApproval,
        DateTimeOffset startedAt,
        Guid? userId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ExecutionUserId = userId,
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

    public void SetExecutionPrincipal(Guid executionUserId)
    {
        if (executionUserId == Guid.Empty)
            throw new ArgumentException("execution_user_id_required", nameof(executionUserId));

        ExecutionUserId = executionUserId;
    }

    public void Approve(Guid executionUserId)
    {
        if (Status != AgentSessionStatuses.PendingApproval)
            throw new InvalidOperationException("Only pending orchestration plans can be approved.");

        SetExecutionPrincipal(executionUserId);
        Status = AgentSessionStatuses.Running;
    }

    public void UpdatePlan(string planJson, Guid executionUserId)
    {
        if (Status is not (AgentSessionStatuses.Draft or AgentSessionStatuses.PendingApproval or AgentSessionStatuses.Paused))
            throw new InvalidOperationException("Only draft, pending, or paused orchestration plans can be edited.");

        SetExecutionPrincipal(executionUserId);
        PlanJson = string.IsNullOrWhiteSpace(planJson) ? "{}" : planJson;
    }

    public void RequestPause()
    {
        if (Status != AgentSessionStatuses.Running)
            throw new InvalidOperationException("Only running orchestration plans can request a pause.");

        Status = AgentSessionStatuses.PauseRequested;
    }

    public void AcknowledgePause()
    {
        if (Status != AgentSessionStatuses.PauseRequested)
            throw new InvalidOperationException("Only requested orchestration pauses can be acknowledged.");

        Status = AgentSessionStatuses.Paused;
    }

    public void Resume(Guid executionUserId)
    {
        if (Status != AgentSessionStatuses.Paused)
            throw new InvalidOperationException("Only paused orchestration plans can be resumed.");

        SetExecutionPrincipal(executionUserId);
        Status = AgentSessionStatuses.Running;
    }

    /// <summary>
    /// Dừng phiên tại chỗ để người dùng can thiệp (sửa output / chạy lại / bỏ qua bước lỗi) thay vì
    /// để orchestrator tự lập lại kế hoạch. Nhận cả PauseRequested vì người dùng có thể vừa bấm tạm dừng.
    /// </summary>
    public void PauseForIntervention()
    {
        if (Status is not (AgentSessionStatuses.Running or AgentSessionStatuses.PauseRequested))
            throw new InvalidOperationException("Only running or pause-requested orchestration plans can pause for intervention.");

        Status = AgentSessionStatuses.Paused;
    }

    /// <summary>
    /// Finalize a running planning placeholder with the generated plan. Moves to pending approval when
    /// the tenant or a cost pre-flight requires it; otherwise stays running for auto-execution.
    /// </summary>
    public void ApplyGeneratedPlan(string planJson, bool requiresApproval)
    {
        if (Status != AgentSessionStatuses.Running)
            throw new InvalidOperationException("Only a running planning session can receive a generated plan.");

        PlanJson = string.IsNullOrWhiteSpace(planJson) ? "{}" : planJson;
        RequiresApproval = requiresApproval;
        if (requiresApproval)
            Status = AgentSessionStatuses.PendingApproval;
    }

    public int ApplyReplan(string planJson, int expectedGeneration)
    {
        if (Status != AgentSessionStatuses.Running)
            throw new InvalidOperationException("Only a running orchestration session can be replanned.");
        if (ReplanCount != expectedGeneration)
            throw new OrchestrationPlanGenerationMismatchException();
        if (string.IsNullOrWhiteSpace(planJson))
            throw new ArgumentException("plan_json_required", nameof(planJson));

        PlanJson = planJson;
        ReplanCount = checked(ReplanCount + 1);
        return ReplanCount;
    }

    /// <summary>Persist the executed plan (with per-task statuses/outputs) after a run. No state guard.</summary>
    public void RecordRun(string planJson) =>
        PlanJson = string.IsNullOrWhiteSpace(planJson) ? "{}" : planJson;

    public void Cancel(DateTimeOffset at)
    {
        if (Status == AgentSessionStatuses.Cancelled)
            return;
        EnsureTerminalRequestCanStart();
        Status = AgentSessionStatuses.Cancelled;
        FinishedAt = at;
    }

    public void DeferCancellation(int expectedGeneration, DateTimeOffset at)
    {
        EnsureExpectedGeneration(expectedGeneration);
        EnsureTerminalRequestCanStart();
        Status = AgentSessionStatuses.Cancelling;
        PendingTerminalGeneration = expectedGeneration;
        PendingTerminalRequestedAt = at;
        PendingTerminalReason = null;
    }

    public void DeferFailure(string reason, int expectedGeneration, DateTimeOffset at)
    {
        EnsureExpectedGeneration(expectedGeneration);
        EnsureTerminalRequestCanStart();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("terminal_reason_required", nameof(reason));

        Status = AgentSessionStatuses.Failing;
        PendingTerminalGeneration = expectedGeneration;
        PendingTerminalRequestedAt = at;
        PendingTerminalReason = reason.Trim() is { Length: > MaxPendingTerminalReasonLength } normalized
            ? normalized[..MaxPendingTerminalReasonLength]
            : reason.Trim();
    }

    public void FinalizeDeferredTerminal(DateTimeOffset at)
    {
        if (PendingTerminalGeneration is null || PendingTerminalRequestedAt is null)
            throw new InvalidOperationException("orchestration_terminal_intent_missing");

        Status = Status switch
        {
            AgentSessionStatuses.Cancelling => AgentSessionStatuses.Cancelled,
            AgentSessionStatuses.Failing => AgentSessionStatuses.Failed,
            _ => throw new InvalidOperationException("orchestration_terminal_intent_not_pending"),
        };
        FinishedAt = at;
        PendingTerminalGeneration = null;
        PendingTerminalRequestedAt = null;
        PendingTerminalReason = null;
    }

    public void Finish(DateTimeOffset at)
    {
        if (Status != AgentSessionStatuses.Running)
            throw new InvalidOperationException("Only a running orchestration session can complete.");

        Status = AgentSessionStatuses.Completed;
        FinishedAt = at;
    }

    public void Fail(DateTimeOffset at)
    {
        if (Status is not (AgentSessionStatuses.Running
            or AgentSessionStatuses.PendingApproval
            or AgentSessionStatuses.PauseRequested
            or AgentSessionStatuses.Paused))
        {
            throw new InvalidOperationException("Only active orchestration sessions can fail.");
        }

        Status = AgentSessionStatuses.Failed;
        FinishedAt = at;
    }

    public void Archive(DateTimeOffset at)
    {
        if (Status is not (AgentSessionStatuses.Completed or AgentSessionStatuses.Failed or AgentSessionStatuses.Cancelled))
            throw new InvalidOperationException("Only completed, failed, or cancelled orchestration sessions can be archived. Cancel running sessions first.");

        ArchivedAt = at;
    }

    private void EnsureExpectedGeneration(int expectedGeneration)
    {
        if (ReplanCount != expectedGeneration)
            throw new OrchestrationPlanGenerationMismatchException();
    }

    private void EnsureTerminalRequestCanStart()
    {
        if (Status is not (AgentSessionStatuses.Running
            or AgentSessionStatuses.PendingApproval
            or AgentSessionStatuses.PauseRequested
            or AgentSessionStatuses.Paused))
        {
            throw new InvalidOperationException("Only active or pending-approval orchestration plans can be terminalized.");
        }
    }

    // ArchivedAt is just a visibility flag, so restoring an archived session is always safe.
    public void Unarchive() => ArchivedAt = null;
}

