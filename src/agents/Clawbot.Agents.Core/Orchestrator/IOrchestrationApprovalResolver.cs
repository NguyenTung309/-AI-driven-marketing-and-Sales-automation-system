namespace Clawbot.Agents.Core.Orchestrator;

// SPEC-16 P4-4: resolves whether a tenant requires human approval before high-risk autonomous actions.
// Implemented in Infrastructure against Tenant.RequireOrchestrationApproval; kept as an interface in Agents.Core
// so the orchestrator stays free of Domain/Infrastructure coupling.
public interface IOrchestrationApprovalResolver
{
    Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default);
}
