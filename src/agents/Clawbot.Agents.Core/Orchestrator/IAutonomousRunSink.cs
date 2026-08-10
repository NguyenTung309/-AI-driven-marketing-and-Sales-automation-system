namespace Clawbot.Agents.Core.Orchestrator;

// Persistence/trace sink so the Core orchestrator stays free of AppDbContext/AgentSession coupling.
// Implementations live in AgentService.
public interface IAutonomousRunSink
{
    Task TraceAsync(Guid tenantId, Guid sessionId, string taskId, string agent, string phase, string message, DateTimeOffset at, CancellationToken ct = default);

    Task PersistPlanAsync(
        Guid tenantId,
        Guid sessionId,
        OrchestrationPlanDocument plan,
        int expectedGeneration,
        bool requiresApproval = false,
        CancellationToken ct = default);

    Task<int> GetPlanGenerationAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);

    Task<int> PersistReplanAndRejectSupersededContentAsync(
        Guid tenantId,
        Guid sessionId,
        int expectedGeneration,
        OrchestrationPlanDocument replacementPlan,
        DateTimeOffset at,
        CancellationToken ct = default);

    Task<bool> IsStoppedAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);

    Task<bool> TryAcknowledgePauseAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);

    // Task lỗi + chính sách "pause": đưa phiên về Paused và báo cho người dùng, thay vì replan tự động.
    Task PauseForInterventionAsync(
        Guid tenantId,
        Guid sessionId,
        string taskId,
        string reason,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default);

    Task<int> FailAndRejectOrphanedContentAsync(
        Guid tenantId,
        Guid sessionId,
        string reason,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default);

    Task CompleteAsync(
        Guid tenantId,
        Guid sessionId,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default);
    Task FailAsync(
        Guid tenantId,
        Guid sessionId,
        string reason,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default);
    Task CancelAsync(
        Guid tenantId,
        Guid sessionId,
        int expectedGeneration,
        byte[]? expectedRowVersion,
        DateTimeOffset at,
        CancellationToken ct = default);
    Task<int> FinalizeDeferredTerminalsAsync(CancellationToken ct = default);
}
