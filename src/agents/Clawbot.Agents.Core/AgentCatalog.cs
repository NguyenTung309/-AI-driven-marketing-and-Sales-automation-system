namespace Clawbot.Agents.Core;

public sealed record AgentCatalogEntry(
    string Code,
    string ShortName,
    string DisplayName,
    string AgentType,
    string Description,
    string InputSchemaJson,
    bool Orchestratable);

public interface IAgentCatalog
{
    Task<IReadOnlyList<AgentCatalogEntry>> ListAsync(CancellationToken ct = default);
    Task<AgentCatalogEntry> ResolveAsync(string name, CancellationToken ct = default);
}
