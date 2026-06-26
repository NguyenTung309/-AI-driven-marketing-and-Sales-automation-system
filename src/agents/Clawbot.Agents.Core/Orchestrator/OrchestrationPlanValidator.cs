using Clawbot.Agents.Core;

namespace Clawbot.Agents.Core.Orchestrator;

public static class OrchestrationPlanValidator
{
    public const int MaxTaskCount = 50;
    public const int MaxTaskInputChars = 8192;

    public static OrchestrationPlanValidationResult Validate(
        OrchestrationPlanDocument plan,
        IReadOnlyList<AgentCatalogEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);

        if (plan.Tasks.Count == 0)
            return OrchestrationPlanValidationResult.Invalid("empty_plan");
        if (plan.Tasks.Count > MaxTaskCount)
            return OrchestrationPlanValidationResult.Invalid($"too_many_tasks:{plan.Tasks.Count}:{MaxTaskCount}");

        var taskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in plan.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
                return OrchestrationPlanValidationResult.Invalid("task_id_required");
            if (!taskIds.Add(task.Id))
                return OrchestrationPlanValidationResult.Invalid($"duplicate_task:{task.Id}");
            if (InputSize(task.Input) > MaxTaskInputChars)
                return OrchestrationPlanValidationResult.Invalid($"input_too_large:{task.Id}");
            if (!KnownAgent(task.Agent, catalog))
                return OrchestrationPlanValidationResult.Invalid(
                    $"unknown_agent:{task.Id}:{task.Agent}. Agent hợp lệ: {AllowedCodes(catalog)}");
        }

        foreach (var task in plan.Tasks)
        {
            foreach (var dependency in task.DependsOn)
            {
                if (!taskIds.Contains(dependency))
                    return OrchestrationPlanValidationResult.Invalid($"dangling_dependency:{task.Id}:{dependency}");
            }
        }

        return HasCycle(plan) ? OrchestrationPlanValidationResult.Invalid("cycle_detected") : OrchestrationPlanValidationResult.Valid;
    }

    private static int InputSize(IReadOnlyDictionary<string, string> input) =>
        input.Sum(pair => pair.Key.Length + (pair.Value?.Length ?? 0));

    private static string AllowedCodes(IReadOnlyList<AgentCatalogEntry> catalog) =>
        string.Join(", ", catalog.Where(entry => entry.Orchestratable).Select(entry => entry.Code));

    private static bool KnownAgent(string name, IReadOnlyList<AgentCatalogEntry> catalog) =>
        catalog.Any(entry => entry.Orchestratable &&
            (string.Equals(entry.Code, name, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.ShortName, name, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.AgentType, name, StringComparison.OrdinalIgnoreCase)));

    private static bool HasCycle(OrchestrationPlanDocument plan)
    {
        var byId = plan.Tasks.ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return plan.Tasks.Any(task => Visit(task.Id));

        bool Visit(string id)
        {
            if (visited.Contains(id)) return false;
            if (!visiting.Add(id)) return true;

            foreach (var dependency in byId[id].DependsOn)
            {
                if (Visit(dependency)) return true;
            }

            visiting.Remove(id);
            visited.Add(id);
            return false;
        }
    }
}
