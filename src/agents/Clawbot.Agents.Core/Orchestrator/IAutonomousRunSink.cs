namespace Clawbot.Agents.Core.Orchestrator;

// Persistence/trace sink so the Core orchestrator stays free of AppDbContext/AgentSession coupling.
// Implementations live in AgentService.
public interface IAutonomousRunSink
{
    Task TraceAsync(Guid tenantId, Guid sessionId, string taskId, string agent, string phase, string message, DateTimeOffset at, CancellationToken ct = default);

    Task PersistPlanAsync(Guid tenantId, Guid sessionId, OrchestrationPlanDocument plan, bool requiresApproval = false, CancellationToken ct = default);

    Task<bool> IsStoppedAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);

    Task CompleteAsync(Guid tenantId, Guid sessionId, DateTimeOffset at, CancellationToken ct = default);
    Task FailAsync(Guid tenantId, Guid sessionId, string reason, DateTimeOffset at, CancellationToken ct = default);
    Task CancelAsync(Guid tenantId, Guid sessionId, DateTimeOffset at, CancellationToken ct = default);
}
