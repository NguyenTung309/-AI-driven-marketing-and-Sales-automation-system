namespace Clawbot.Agents.Core;

public sealed record AgentTask(
    string Id,
    string AgentName,
    string Description,
    IReadOnlyDictionary<string, string> Input);

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
