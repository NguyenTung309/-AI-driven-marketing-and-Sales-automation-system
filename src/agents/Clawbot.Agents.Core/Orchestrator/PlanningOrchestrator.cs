using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Clawbot.Agents.Core.Orchestrator;

public sealed record OrchestratorPlan(string SessionId, IReadOnlyList<AgentTask> Tasks);
public sealed record OrchestratorTraceEntry(string TaskId, string Phase, string Message, DateTimeOffset At);

public sealed class PlanningOrchestrator(AgentRegistry registry)
{
    private readonly ConcurrentDictionary<string, List<OrchestratorTraceEntry>> _traces = new();
    private readonly ConcurrentDictionary<string, string> _sessionTenants = new();

    public OrchestratorPlan Plan(string tenantId, string goal)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var cleanGoal = (goal ?? string.Empty).Trim();
        _sessionTenants[sessionId] = tenantId;
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

    public IReadOnlyList<OrchestratorTraceEntry> GetTrace(string sessionId, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(tenantId))
            return [];
        if (!_sessionTenants.TryGetValue(sessionId, out var owner) || !string.Equals(owner, tenantId, StringComparison.Ordinal))
            return [];
        if (!_traces.TryGetValue(sessionId, out var entries))
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
            while (index >= 0)
            {
                if (IsReferenceBoundary(goal, index - 1) && IsReferenceBoundary(goal, index + candidate.Length))
                    return index;

                index = goal.IndexOf(candidate, index + candidate.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return -1;
    }

    private static bool IsReferenceBoundary(string value, int index) =>
        index < 0 || index >= value.Length || !char.IsLetterOrDigit(value[index]);

    private static IEnumerable<string> AgentReferenceCandidates(string agentName)
    {
        yield return agentName;
        if (!agentName.EndsWith('s'))
            yield return $"{agentName}s";

        if (agentName.Contains('_', StringComparison.Ordinal))
        {
            var hyphenated = agentName.Replace('_', '-');
            var spaced = agentName.Replace('_', ' ');
            yield return hyphenated;
            yield return spaced;
            if (!hyphenated.EndsWith('s'))
                yield return $"{hyphenated}s";
            if (!spaced.EndsWith('s'))
                yield return $"{spaced}s";
        }
    }
}
