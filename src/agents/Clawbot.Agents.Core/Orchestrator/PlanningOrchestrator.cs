using System.Runtime.CompilerServices;

namespace Clawbot.Agents.Core.Orchestrator;

public sealed record OrchestratorPlan(string SessionId, IReadOnlyList<AgentTask> Tasks);

public sealed class PlanningOrchestrator(AgentRegistry registry)
{
    public OrchestratorPlan Plan(string tenantId, string goal)
    {
        // TODO(student): use Semantic Kernel planner to decompose `goal` into AgentTask[]
        // referencing registry.Names.
        _ = tenantId;
        _ = goal;
        _ = registry;
        return new OrchestratorPlan(Guid.NewGuid().ToString(), Array.Empty<AgentTask>());
    }

    public async IAsyncEnumerable<AgentResult> ExecuteAsync(
        OrchestratorPlan plan,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var task in plan.Tasks)
        {
            var agent = registry.Resolve(task.AgentName);
            yield return await agent.ExecuteAsync(task, ct);
        }
    }
}
