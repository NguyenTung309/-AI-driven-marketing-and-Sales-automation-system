using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;

namespace Clawbot.AgentService.Services;

// Default IAutonomousPlanner: delegates to SemanticKernelPlanGenerator under an LLM call scope.
public sealed class AutonomousPlanner(
    SemanticKernelPlanGenerator generator,
    ILlmCallScope llmScope) : IAutonomousPlanner
{
    private const string OrchestratorAgentCode = "orchestrator";

    public Task<OrchestrationPlanDocument> PlanAsync(Guid tenantId, string goal, IReadOnlyList<AgentCatalogEntry> catalog, CancellationToken ct = default)
    {
        using (llmScope.Begin(tenantId, OrchestratorAgentCode))
        {
            return generator.GenerateAsync(goal, catalog, ct);
        }
    }

    public Task<OrchestrationPlanDocument> ReplanAsync(Guid tenantId, string goal, IReadOnlyList<AgentCatalogEntry> catalog, IReadOnlyList<OrchestrationPlanTask> failed, CancellationToken ct = default)
    {
        var replanGoal = BuildReplanGoal(goal, failed);
        using (llmScope.Begin(tenantId, OrchestratorAgentCode))
        {
            return generator.GenerateAsync(replanGoal, catalog, ct);
        }
    }

    private static string BuildReplanGoal(string goal, IReadOnlyList<OrchestrationPlanTask> failed)
    {
        var failedSummary = string.Join("; ", failed.Select(f => $"{f.Agent}:{f.Error ?? "failed"}"));
        return $"Original goal: {goal}. Previous tasks failed ({failedSummary}). Produce a revised plan that avoids the failed approach.";
    }
}
