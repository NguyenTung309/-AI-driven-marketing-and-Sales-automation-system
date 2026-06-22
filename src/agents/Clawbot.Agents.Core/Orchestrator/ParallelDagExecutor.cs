namespace Clawbot.Agents.Core.Orchestrator;

public sealed class ParallelDagExecutor(
    IReadOnlyDictionary<string, IAgent> agents,
    int maxConcurrency,
    ParallelDagExecutor.ReplanCallback? replanner = null,
    int maxReplans = 0,
    ParallelDagExecutor.TaskProgressCallback? onTaskProgress = null,
    ParallelDagExecutor.TaskStartedCallback? onTaskStarted = null)
{
    /// <summary>
    /// Bounded re-plan hook (T3.4): invoked after a wave that produced failures. Returns a patched plan
    /// (preserving completed task statuses/outputs) or null to stop re-planning and fall through to skip policy.
    /// </summary>
    public delegate Task<OrchestrationPlanDocument?> ReplanCallback(
        OrchestrationPlanDocument current,
        IReadOnlyList<OrchestrationPlanTask> failedTasks,
        int replanAttempt,
        CancellationToken ct);

    /// <summary>Called after each task status is merged into the current immutable plan.</summary>
    public delegate Task TaskProgressCallback(
        OrchestrationPlanDocument current,
        OrchestrationPlanTask task,
        AgentResult result,
        CancellationToken ct);

    public delegate Task TaskStartedCallback(OrchestrationPlanTask task, CancellationToken ct);

    private readonly IReadOnlyDictionary<string, IAgent> _agents = agents;
    private readonly int _maxConcurrency = Math.Max(1, maxConcurrency);
    private readonly ReplanCallback? _replanner = replanner;
    private readonly int _maxReplans = Math.Max(0, maxReplans);
    private readonly TaskProgressCallback? _onTaskProgress = onTaskProgress;
    private readonly TaskStartedCallback? _onTaskStarted = onTaskStarted;

    public async Task<OrchestrationPlanDocument> ExecuteAsync(
        OrchestrationPlanDocument plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var current = plan;
        var replansUsed = 0;
        var (completed, failed, remaining) = Partition(current);

        while (remaining.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var ready = current.Tasks
                .Where(task => remaining.Contains(task.Id))
                .Where(task => task.DependsOn.All(dep => completed.Contains(dep) || failed.Contains(dep)))
                .Take(_maxConcurrency)
                .ToArray();

            if (ready.Length == 0)
                break;

            var runnable = ready.Where(task => task.DependsOn.All(dep => !failed.Contains(dep))).ToArray();
            var skipped = ready.Except(runnable).ToArray();
            foreach (var task in skipped)
            {
                var result = new AgentResult(task.Id, Success: false, Output: string.Empty, Error: "dependency_failed");
                current = current.WithTaskStatus(task.Id, "skipped", null, "dependency_failed");
                failed.Add(task.Id);
                remaining.Remove(task.Id);
                if (_onTaskProgress is not null)
                    await _onTaskProgress(current, task, result, ct).ConfigureAwait(false);
            }

            if (_onTaskStarted is not null)
            {
                foreach (var task in runnable)
                    await _onTaskStarted(task, ct).ConfigureAwait(false);
            }

            var results = await Task.WhenAll(runnable.Select(task => ExecuteTaskAsync(task, ct))).ConfigureAwait(false);
            var newFailures = new List<OrchestrationPlanTask>();
            foreach (var (task, result) in results)
            {
                if (result.Success)
                {
                    current = current.WithTaskStatus(task.Id, "completed", result.Output, null);
                    completed.Add(task.Id);
                }
                else if (IsExecutionStop(result.Error))
                {
                    if (IsCostStop(result.Error))
                    {
                        current = current.WithTaskStatus(task.Id, "failed", result.Output, result.Error);
                        remaining.Remove(task.Id);
                        if (_onTaskProgress is not null)
                            await _onTaskProgress(current, task, result, ct).ConfigureAwait(false);
                    }

                    return current;
                }
                else
                {
                    current = current.WithTaskStatus(task.Id, "failed", result.Output, result.Error);
                    failed.Add(task.Id);
                    newFailures.Add(task with { Error = result.Error });
                }

                remaining.Remove(task.Id);
                if (_onTaskProgress is not null)
                    await _onTaskProgress(current, task, result, ct).ConfigureAwait(false);
            }

            if (newFailures.Count > 0 && _replanner is not null && replansUsed < _maxReplans)
            {
                var patched = await _replanner(current, newFailures, replansUsed + 1, ct).ConfigureAwait(false);
                if (patched is not null)
                {
                    replansUsed++;
                    current = patched;
                    (completed, failed, remaining) = Partition(current);
                }
            }
        }

        foreach (var taskId in remaining.ToArray())
            current = current.WithTaskStatus(taskId, "skipped", null, "not_reachable");

        return current;
    }

    private static bool IsExecutionStop(string? error) =>
        string.Equals(error, "paused", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(error, "cancelled", StringComparison.OrdinalIgnoreCase) ||
        IsCostStop(error);

    private static bool IsCostStop(string? error) =>
        string.Equals(error, "cost_cap_midrun", StringComparison.OrdinalIgnoreCase);

    private static (HashSet<string> Completed, HashSet<string> Failed, HashSet<string> Remaining) Partition(
        OrchestrationPlanDocument plan)
    {
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in plan.Tasks)
        {
            switch (task.Status?.ToLowerInvariant())
            {
                case "completed":
                    completed.Add(task.Id);
                    break;
                case "failed":
                case "skipped":
                    failed.Add(task.Id);
                    break;
                default:
                    remaining.Add(task.Id);
                    break;
            }
        }

        return (completed, failed, remaining);
    }

    private async Task<(OrchestrationPlanTask Task, AgentResult Result)> ExecuteTaskAsync(
        OrchestrationPlanTask task,
        CancellationToken ct)
    {
        if (!_agents.TryGetValue(task.Agent, out var agent))
        {
            return (task, new AgentResult(task.Id, Success: false, Output: string.Empty,
                Error: $"Agent '{task.Agent}' is not registered."));
        }

        try
        {
            var result = await agent.ExecuteAsync(
                new AgentTask(task.Id, task.Agent, task.Description, task.Input), ct).ConfigureAwait(false);
            return (task, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (task, new AgentResult(task.Id, Success: false, Output: string.Empty, Error: ex.Message));
        }
    }
}
