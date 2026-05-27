namespace Clawbot.Agents.Core;

public sealed class AgentRegistry
{
    private readonly Dictionary<string, IAgent> _agents;

    public AgentRegistry(IEnumerable<IAgent> agents)
    {
        _agents = agents.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IAgent Resolve(string name) =>
        _agents.TryGetValue(name, out var agent)
            ? agent
            : throw new KeyNotFoundException($"Agent '{name}' is not registered.");

    public IEnumerable<string> Names => _agents.Keys;
}
