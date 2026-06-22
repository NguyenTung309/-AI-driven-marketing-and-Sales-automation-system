namespace Clawbot.Agents.Core.Orchestrator;

/// <summary>
/// Merges a re-planned DAG into the current run (T3.4): completed tasks are preserved verbatim,
/// everything else is replaced by the freshly generated tasks (re-id'd with an attempt prefix so
/// they never collide with the preserved ids, and reset to pending).
/// </summary>
public static class OrchestrationReplan
{
    public static OrchestrationPlanDocument Merge(
        OrchestrationPlanDocument current,
        OrchestrationPlanDocument regenerated,
        int attempt)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(regenerated);

        var completed = current.Tasks
            .Where(task => string.Equals(task.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var prefix = $"r{attempt}-";
        var regeneratedIds = regenerated.Tasks
            .Select(task => task.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fresh = regenerated.Tasks
            .Select(task => task with
            {
                Id = prefix + task.Id,
                DependsOn = task.DependsOn
                    .Select(dep => regeneratedIds.Contains(dep) ? prefix + dep : dep)
                    .ToArray(),
                Status = "pending",
                Output = null,
                Error = null,
            })
            .ToArray();

        return current with { Tasks = completed.Concat(fresh).ToArray() };
    }

    public static string BuildReplanGoal(
        string goal,
        IReadOnlyList<OrchestrationPlanTask> failedTasks)
    {
        var failures = string.Join("; ", failedTasks.Select(task =>
            $"{task.Agent}:{task.Error ?? "failed"}"));
        return $"{goal}\n\nReplan: the following steps failed and must be replaced with an alternative approach: {failures}";
    }
}
