using System.Text.Json;

namespace Clawbot.Agents.Core.Orchestrator;

/// <summary>Serializes the plan DAG to/from the <c>AgentSession.PlanJson</c> column (camelCase web JSON).</summary>
public static class OrchestrationPlanJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(OrchestrationPlanDocument plan) =>
        JsonSerializer.Serialize(plan, Options);

    public static OrchestrationPlanDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<OrchestrationPlanDocument>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
