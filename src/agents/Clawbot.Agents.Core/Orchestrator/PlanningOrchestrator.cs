using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Clawbot.Agents.Core.Orchestrator;

public sealed record OrchestratorPlan(string SessionId, IReadOnlyList<AgentTask> Tasks);
public sealed record OrchestratorTraceEntry(string TaskId, string Phase, string Message, DateTimeOffset At);

public sealed class PlanningOrchestrator(AgentRegistry registry)
{
    private readonly ConcurrentDictionary<string, List<OrchestratorTraceEntry>> _traces = new();

    public OrchestratorPlan Plan(string tenantId, string goal)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var cleanGoal = (goal ?? string.Empty).Trim();
        var agentNames = registry.Names
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = agentNames
            .Select(name => new { Name = name, Index = IndexOfAgentReference(cleanGoal, name) })
            .Where(item => item.Index >= 0)
            .OrderBy(item => item.Index)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Name)
            .ToArray();

        if (selected.Length == 0)
            selected = agentNames;

        var tasks = selected
            .Select((name, index) => new AgentTask(
                $"task-{index + 1:000}",
                name,
                string.IsNullOrWhiteSpace(cleanGoal)
                    ? $"Run {name} agent."
                    : $"Run {name} agent for goal: {cleanGoal}",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant_id"] = tenantId,
                    ["goal"] = cleanGoal,
                    ["agent"] = name,
                }))
            .ToArray();

        AppendTrace(sessionId, string.Empty, "planned",
            tasks.Length == 0
                ? "No registered agents are available for this plan."
                : $"Planned {tasks.Length} task(s): {string.Join(", ", tasks.Select(t => t.AgentName))}.");

        return new OrchestratorPlan(sessionId, tasks);
    }

    public async IAsyncEnumerable<AgentResult> ExecuteAsync(
        OrchestratorPlan plan,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var task in plan.Tasks)
        {
            var agent = registry.Resolve(task.AgentName);
            AppendTrace(plan.SessionId, task.Id, "started", $"Starting {task.AgentName}.");
            AgentResult result;
            try
            {
                result = await agent.ExecuteAsync(task, ct);
            }
            catch (Exception ex)
            {
                AppendTrace(plan.SessionId, task.Id, "failed", ex.Message);
                throw;
            }

            AppendTrace(
                plan.SessionId,
                task.Id,
                result.Success ? "completed" : "failed",
                result.Success ? result.Output : result.Error ?? result.Output);
            yield return result;
        }
    }

    public IReadOnlyList<OrchestratorTraceEntry> GetTrace(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !_traces.TryGetValue(sessionId, out var entries))
            return [];

        lock (entries)
        {
            return entries.ToArray();
        }
    }

    private void AppendTrace(string sessionId, string taskId, string phase, string message)
    {
        var entries = _traces.GetOrAdd(sessionId, _ => []);
        lock (entries)
        {
            entries.Add(new OrchestratorTraceEntry(taskId, phase, message, DateTimeOffset.UtcNow));
        }
    }

    private static int IndexOfAgentReference(string goal, string agentName)
    {
        foreach (var candidate in AgentReferenceCandidates(agentName))
        {
            var index = goal.IndexOf(candidate, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                return index;
        }

        return -1;
    }

    private static IEnumerable<string> AgentReferenceCandidates(string agentName)
    {
        yield return agentName;
        if (agentName.Contains('_', StringComparison.Ordinal))
        {
            yield return agentName.Replace('_', '-');
            yield return agentName.Replace('_', ' ');
        }
    }
}
