namespace Clawbot.Agents.Core.Orchestrator;

// V2 sub-agent catalog: data-defined agents (agent_definitions) projected to planner-facing entries.
// Distinct from V1 IAgentCatalog (which reads the fixed agents table) so V2 personas drop in without
// rewiring V1.
public interface IAgentDefinitionCatalog
{
    Task<IReadOnlyList<AgentDefinitionCatalogEntry>> ListAsync(Guid tenantId, CancellationToken ct = default);

    Task<AgentDefinitionCatalogEntry?> FindByCodeAsync(Guid tenantId, string code, CancellationToken ct = default);
}
