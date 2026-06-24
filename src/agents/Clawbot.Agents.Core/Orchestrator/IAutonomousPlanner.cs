namespace Clawbot.Agents.Core.Orchestrator;

// Planner abstraction so AutonomousOrchestrator is testable without an LLM.
// The default implementation wraps SemanticKernelPlanGenerator under an LLM call scope.
public interface IAutonomousPlanner
{
    Task<OrchestrationPlanDocument> PlanAsync(Guid tenantId, string goal, IReadOnlyList<AgentCatalogEntry> catalog, CancellationToken ct = default);

    Task<OrchestrationPlanDocument> ReplanAsync(Guid tenantId, string goal, IReadOnlyList<AgentCatalogEntry> catalog, IReadOnlyList<OrchestrationPlanTask> failed, CancellationToken ct = default);
}
