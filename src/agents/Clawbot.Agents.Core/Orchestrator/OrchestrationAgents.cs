namespace Clawbot.Agents.Core.Orchestrator;

/// <summary>
/// Builds the agent lookup the executor uses, keyed by every alias the planner might emit for a task:
/// the adapter's canonical Name plus the catalog Code / ShortName / AgentType that resolve to it.
/// </summary>
public static class OrchestrationAgents
{
    public static IReadOnlyDictionary<string, IAgent> Build(
        IEnumerable<IAgent> adapters,
        IReadOnlyList<AgentCatalogEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(catalog);

        var adapterList = adapters.ToArray();
        var byName = adapterList.ToDictionary(agent => agent.Name, agent => agent, StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase);
        foreach (var agent in adapterList)
            map[agent.Name] = agent;

        foreach (var entry in catalog)
        {
            if (!byName.TryGetValue(entry.Code, out var agent))
                continue;

            map[entry.Code] = agent;
            if (!string.IsNullOrWhiteSpace(entry.ShortName))
                map[entry.ShortName] = agent;
            if (!string.IsNullOrWhiteSpace(entry.AgentType))
                map[entry.AgentType] = agent;
        }

        return map;
    }
}
