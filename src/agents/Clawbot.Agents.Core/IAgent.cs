namespace Clawbot.Agents.Core;

public sealed record AgentTask(
    string Id,
    string AgentName,
    string Description,
    IReadOnlyDictionary<string, string> Input,
    // Orchestrator co the sinh system prompt rieng cho sub-agent theo tung task; null -> dung mac dinh cua agent.
    string? RoleInstruction = null);

public sealed record AgentResult(
    string TaskId,
    bool Success,
    string Output,
    string? Error);

public interface IAgent
{
    string Name { get; }
    Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct);
}
