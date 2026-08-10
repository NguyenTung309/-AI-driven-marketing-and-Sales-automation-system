using Clawbot.Agents.Core;

namespace Clawbot.AgentService.Services;

public static class DefaultAgentRegistry
{
    private static readonly (string Name, string Service)[] RuntimeAgents =
    [
        ("chat", nameof(ChatAgentGrpcService)),
        ("sale_assist", nameof(SaleAssistAgentGrpcService)),
        ("lead", nameof(LeadAgentGrpcService)),
        ("content", nameof(ContentAgentGrpcService)),
        ("research", nameof(ResearchAgentGrpcService)),
        ("docs", nameof(DocsAgentGrpcService)),
        ("report", nameof(ReportAgentGrpcService)),
    ];

    public static AgentRegistry Create() =>
        new(RuntimeAgents.Select(agent => new CatalogAgent(agent.Name, agent.Service)));

    private sealed class CatalogAgent(string name, string serviceName) : IAgent
    {
        public string Name { get; } = name;

        public Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new AgentResult(
                task.Id,
                Success: true,
                Output: $"Task {task.Id} for agent '{Name}' is routed to {serviceName}.",
                Error: null));
        }
    }
}
