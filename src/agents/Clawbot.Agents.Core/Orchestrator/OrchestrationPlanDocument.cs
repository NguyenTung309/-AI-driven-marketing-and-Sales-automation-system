namespace Clawbot.Agents.Core.Orchestrator;

public sealed record OrchestrationPlanTask(
    string Id,
    string Agent,
    string Description,
    IReadOnlyDictionary<string, string> Input,
    IReadOnlyList<string> DependsOn,
    string Status,
    string? Output,
    string? Error);

public sealed record OrchestrationPlanDocument(int Version, IReadOnlyList<OrchestrationPlanTask> Tasks)
{
    public OrchestrationPlanDocument WithTaskStatus(string taskId, string status, string? output, string? error) =>
        this with
        {
            Tasks = Tasks
                .Select(task => task.Id == taskId
                    ? task with { Status = status, Output = output, Error = error }
                    : task)
                .ToArray()
        };
}

public sealed record OrchestrationPlanValidationResult(bool IsValid, string? Error)
{
    public static OrchestrationPlanValidationResult Valid { get; } = new(true, null);
    public static OrchestrationPlanValidationResult Invalid(string error) => new(false, error);
}
