using Clawbot.Agents.Core.Skills.Nlp;

namespace Clawbot.Agents.Core.Orchestrator;

public static class OrchestrationPlanRedactor
{
    public static async Task<OrchestrationPlanDocument> RedactAsync(
        OrchestrationPlanDocument plan,
        IPiiRedactor redactor,
        CancellationToken ct = default)
    {
        var redactedTasks = new List<OrchestrationPlanTask>(plan.Tasks.Count);
        foreach (var task in plan.Tasks)
        {
            var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in task.Input)
                input[pair.Key] = await RedactTextAsync(pair.Value, redactor, ct).ConfigureAwait(false);

            redactedTasks.Add(task with
            {
                Description = await RedactTextAsync(task.Description, redactor, ct).ConfigureAwait(false),
                Input = input,
            });
        }

        return plan with { Tasks = redactedTasks };
    }

    private static async Task<string> RedactTextAsync(string? text, IPiiRedactor redactor, CancellationToken ct) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : (await redactor.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;
}
