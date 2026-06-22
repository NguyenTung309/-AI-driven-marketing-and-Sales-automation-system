using System.Text.Json;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;

namespace Clawbot.Agents.Core.Orchestrator;

/// <summary>Result of planning: the persisted-ready session, the redacted plan, and cost pre-flight outcome.</summary>
public sealed record OrchestrationPlanResult(
    AgentSession Session,
    OrchestrationPlanDocument Plan,
    bool CostBlocked,
    string? CostReason);

/// <summary>
/// Single-shot SK planner glue (T2.4): generate a DAG from a NL goal, validate it against the
/// orchestratable catalog, PII-redact goal + task descriptions before persist, snapshot the tenant
/// approval requirement, and run a cost pre-flight for auto-run. Persistence/execution is wired by callers.
/// </summary>
public sealed class SemanticKernelOrchestrator(
    IAgentCatalog catalog,
    SemanticKernelPlanGenerator planGenerator,
    IPiiRedactor redactor,
    OrchestratorCostGuard costGuard)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Conservative per-task USD estimate used for the auto-run cost pre-flight.</summary>
    public const decimal PerTaskEstimateUsd = 0.05m;

    private readonly IAgentCatalog _catalog = catalog;
    private readonly SemanticKernelPlanGenerator _planGenerator = planGenerator;
    private readonly IPiiRedactor _redactor = redactor;
    private readonly OrchestratorCostGuard _costGuard = costGuard;

    public async Task<OrchestrationPlanResult> PlanAsync(
        Guid tenantId,
        string goal,
        bool requireApproval,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        var entries = (await _catalog.ListAsync(ct).ConfigureAwait(false))
            .Where(entry => entry.Orchestratable)
            .ToArray();

        var plan = await _planGenerator.GenerateAsync(goal, entries, ct).ConfigureAwait(false);

        var redactedGoal = await RedactAsync(goal, ct).ConfigureAwait(false);
        var redactedPlan = await OrchestrationPlanRedactor.RedactAsync(plan, _redactor, ct).ConfigureAwait(false);

        var costBlocked = false;
        string? costReason = null;
        var requiresApproval = requireApproval;
        if (!requireApproval)
        {
            var estimate = PerTaskEstimateUsd * redactedPlan.Tasks.Count;
            var guard = await _costGuard.CanStartAsync(tenantId, estimate, at, ct).ConfigureAwait(false);
            if (!guard.Allowed)
            {
                costBlocked = true;
                costReason = guard.Reason;
                requiresApproval = true;
            }
        }

        var planJson = JsonSerializer.Serialize(redactedPlan, JsonOptions);
        var session = AgentSession.CreatePlan(tenantId, redactedGoal, planJson, requiresApproval, at);
        return new OrchestrationPlanResult(session, redactedPlan, costBlocked, costReason);
    }

    private async Task<string> RedactAsync(string? text, CancellationToken ct) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : (await _redactor.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;
}
