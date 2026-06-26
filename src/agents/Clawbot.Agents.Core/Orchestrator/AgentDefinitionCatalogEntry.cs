namespace Clawbot.Agents.Core.Orchestrator;

// V2 sub-agent catalog entry: carries the agent_definition id (needed for A2A mailbox FK)
// alongside the planner-facing fields. Projected to AgentCatalogEntry for the SK planner.
public sealed record AgentDefinitionCatalogEntry(
    Guid Id,
    string Code,
    string ShortName,
    string DisplayName,
    string AgentType,
    string Description,
    string InputSchemaJson,
    bool Orchestratable,
    string? KbModuleCode)
{
    public AgentCatalogEntry ToPlannerEntry() => new(Code, ShortName, DisplayName, AgentType, Description, InputSchemaJson, Orchestratable);
}
