using System.Text.Json;
using Clawbot.Agents.Core;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Clawbot.Agents.Core.Orchestrator;

public sealed class SemanticKernelPlanGenerator(IChatCompletionService chat)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IChatCompletionService _chat = chat;

    public async Task<OrchestrationPlanDocument> GenerateAsync(
        string goal,
        IReadOnlyList<AgentCatalogEntry> catalog,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var history = new ChatHistory(BuildSystemPrompt(catalog));
        history.AddUserMessage((goal ?? string.Empty).Trim());

        var replies = await _chat.GetChatMessageContentsAsync(history, cancellationToken: ct).ConfigureAwait(false);
        var json = replies.Count > 0 ? replies[0].Content : null;
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Planner returned invalid plan JSON.");

        OrchestrationPlanDocument? plan;
        try
        {
            plan = JsonSerializer.Deserialize<OrchestrationPlanDocument>(NormalizeJson(json), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Planner returned invalid plan JSON.", ex);
        }

        if (plan is null)
            throw new InvalidOperationException("Planner returned invalid plan JSON.");

        var validation = OrchestrationPlanValidator.Validate(plan, catalog);
        if (!validation.IsValid)
            throw new InvalidOperationException($"Planner returned invalid plan: {validation.Error}");

        return plan;
    }

    private static string NormalizeJson(string json)
    {
        var trimmed = json.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
            return trimmed;

        var body = trimmed[(firstLineEnd + 1)..].Trim();
        if (body.EndsWith("```", StringComparison.Ordinal))
            body = body[..^3].Trim();

        return body;
    }

    private static string BuildSystemPrompt(IReadOnlyList<AgentCatalogEntry> catalog)
    {
        var agents = string.Join("\n", catalog.Select(agent =>
            $"- {agent.Code} ({agent.ShortName}) type={agent.AgentType}: {agent.Description}; inputSchema={agent.InputSchemaJson}"));
        return "Return only JSON for an OrchestrationPlanDocument with version and tasks. " +
               "Each task must have id, agent, description, input, dependsOn, status, output, error. " +
               "Use status pending for new tasks. Available agents:\n" + agents;
    }
}
