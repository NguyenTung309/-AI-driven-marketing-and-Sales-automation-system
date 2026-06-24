using Clawbot.Agents.Core.Chat;
using Clawbot.SharedKernel.Time;

namespace Clawbot.Agents.Core.Orchestrator;

// Bounded autonomous coordinator: plan -> delegate (A2A) -> execute -> review/replan -> finalize.
// Sequential dependency-ordered execution for V2 baseline (ponytail: MaxConcurrency cap reserved
// for a parallel upgrade). Hard stops: max rounds, per-task cost reservation, cancellation.
public sealed class AutonomousOrchestrator
{
    private readonly IAutonomousPlanner _planner;
    private readonly IAgentDefinitionCatalog _catalog;
    private readonly AgentRegistry _registry;
    private readonly IA2AMailbox _mailbox;
    private readonly OrchestratorCostGuard _costGuard;
    private readonly ILlmCallScope _llmScope;
    private readonly IAutonomousRunSink _sink;
    private readonly IClock _clock;
    private readonly AutonomousOrchestratorOptions _options;

    private const string OrchestratorAgentCode = "orchestrator";

    public AutonomousOrchestrator(
        IAutonomousPlanner planner,
        IAgentDefinitionCatalog catalog,
        AgentRegistry registry,
        IA2AMailbox mailbox,
        OrchestratorCostGuard costGuard,
        ILlmCallScope llmScope,
        IAutonomousRunSink sink,
        IClock clock,
        AutonomousOrchestratorOptions? options = null)
    {
        _planner = planner;
        _catalog = catalog;
        _registry = registry;
        _mailbox = mailbox;
        _costGuard = costGuard;
        _llmScope = llmScope;
        _sink = sink;
        _clock = clock;
        _options = options ?? new AutonomousOrchestratorOptions();
    }

    public async Task<AutonomousRunResult> RunAsync(AutonomousRunRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            return AutonomousRunResult.Failed("goal_required", 0);

        var entries = await _catalog.ListAsync(request.TenantId, ct).ConfigureAwait(false);
        if (entries.Count == 0)
            return AutonomousRunResult.Failed("no_agents", 0);

        OrchestrationPlanDocument plan;
        try
        {
            using (_llmScope.Begin(request.TenantId, OrchestratorAgentCode))
            {
                plan = await _planner.PlanAsync(request.TenantId, request.Goal, entries.Select(e => e.ToPlannerEntry()).ToArray(), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "planned", $"Plan failed: {ex.Message}", _clock.UtcNow, ct).ConfigureAwait(false);
            await _sink.FailAsync(request.TenantId, request.SessionId, "plan_failed", _clock.UtcNow, ct).ConfigureAwait(false);
            return AutonomousRunResult.Failed("plan_failed", 0);
        }

        await _sink.PersistPlanAsync(request.TenantId, request.SessionId, plan, ct).ConfigureAwait(false);
        await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "planned", $"Planned {plan.Tasks.Count} task(s).", _clock.UtcNow, ct).ConfigureAwait(false);

        var preflight = await _costGuard.CanStartAsync(request.TenantId, plan.Tasks.Count * _options.PerTaskEstimateUsd, _clock.UtcNow, ct).ConfigureAwait(false);
        if (!preflight.Allowed)
        {
            await _sink.FailAsync(request.TenantId, request.SessionId, preflight.Reason ?? "cost_cap_preflight", _clock.UtcNow, ct).ConfigureAwait(false);
            return AutonomousRunResult.Failed(preflight.Reason ?? "cost_cap_preflight", 0);
        }

        var byCode = entries.ToDictionary(e => e.Code, StringComparer.OrdinalIgnoreCase);

        for (var round = 1; round <= _options.MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var ready = ReadyTasks(plan);
            var pending = plan.Tasks.Where(t => IsPending(t.Status)).ToArray();
            if (pending.Length == 0)
            {
                var anyFailed = plan.Tasks.Any(t => string.Equals(t.Status, "failed", StringComparison.OrdinalIgnoreCase));
                if (anyFailed)
                    break; // fall through to replan/failed handling below
                await _sink.CompleteAsync(request.TenantId, request.SessionId, _clock.UtcNow, ct).ConfigureAwait(false);
                return AutonomousRunResult.Completed(round);
            }

            if (ready.Count == 0)
            {
                await _sink.FailAsync(request.TenantId, request.SessionId, "dependency_blocked", _clock.UtcNow, ct).ConfigureAwait(false);
                return AutonomousRunResult.Failed("dependency_blocked", round);
            }

            foreach (var task in ready)
            {
                ct.ThrowIfCancellationRequested();
                plan = await ExecuteTaskAsync(request, plan, task, byCode, ct).ConfigureAwait(false);
            }

            var failed = plan.Tasks.Where(t => string.Equals(t.Status, "failed", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (failed.Length == 0)
                continue;

            if (round >= _options.MaxRounds)
            {
                await _sink.FailAsync(request.TenantId, request.SessionId, "max_rounds", _clock.UtcNow, ct).ConfigureAwait(false);
                return AutonomousRunResult.Failed("max_rounds", round);
            }

            await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "re-planned", $"Re-planning after {failed.Length} failed task(s).", _clock.UtcNow, ct).ConfigureAwait(false);
            using (_llmScope.Begin(request.TenantId, OrchestratorAgentCode))
            {
                plan = await _planner.ReplanAsync(request.TenantId, request.Goal, entries.Select(e => e.ToPlannerEntry()).ToArray(), failed, ct).ConfigureAwait(false);
            }
            await _sink.PersistPlanAsync(request.TenantId, request.SessionId, plan, ct).ConfigureAwait(false);
        }

        await _sink.FailAsync(request.TenantId, request.SessionId, "max_rounds", _clock.UtcNow, ct).ConfigureAwait(false);
        return AutonomousRunResult.Failed("max_rounds", _options.MaxRounds);
    }

    private async Task<OrchestrationPlanDocument> ExecuteTaskAsync(
        AutonomousRunRequest request,
        OrchestrationPlanDocument plan,
        OrchestrationPlanTask task,
        Dictionary<string, AgentDefinitionCatalogEntry> byCode,
        CancellationToken ct)
    {
        if (!byCode.TryGetValue(task.Agent, out var definition))
        {
            await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "failed", $"Agent definition '{task.Agent}' not found.", _clock.UtcNow, ct).ConfigureAwait(false);
            return plan.WithTaskStatus(task.Id, "failed", null, "agent_not_found");
        }

        var reservation = await _costGuard.TryReserveAsync(request.TenantId, _options.PerTaskEstimateUsd, _clock.UtcNow, ct).ConfigureAwait(false);
        if (!reservation.Allowed)
        {
            await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "failed", reservation.Reason ?? "cost_cap_midrun", _clock.UtcNow, ct).ConfigureAwait(false);
            return plan.WithTaskStatus(task.Id, "failed", null, reservation.Reason ?? "cost_cap_midrun");
        }

        await _mailbox.SendAsync(request.TenantId, request.SessionId, null, definition.Id, task.Id, "delegate", SerializeTaskInput(task), ct).ConfigureAwait(false);
        await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "started", task.Description, _clock.UtcNow, ct).ConfigureAwait(false);

        AgentResult result;
        try
        {
            using var _costScope = _llmScope.Begin(request.TenantId, task.Agent, _clock.UtcNow, reservation.ReservationId);
            var agent = ResolveAgent(task.Agent);
            result = await agent.ExecuteAsync(ToAgentTask(task, request.TenantId), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _costGuard.ReleaseReservationAsync(request.TenantId, reservation.ReservationId, ct).ConfigureAwait(false);
            await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "failed", ex.Message, _clock.UtcNow, ct).ConfigureAwait(false);
            return plan.WithTaskStatus(task.Id, "failed", null, ex.Message);
        }

        await _costGuard.ReleaseReservationAsync(request.TenantId, reservation.ReservationId, ct).ConfigureAwait(false);

        if (result.Success)
        {
            await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "completed", result.Output, _clock.UtcNow, ct).ConfigureAwait(false);
            return plan.WithTaskStatus(task.Id, "completed", result.Output, null);
        }

        await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "failed", result.Error ?? result.Output, _clock.UtcNow, ct).ConfigureAwait(false);
        return plan.WithTaskStatus(task.Id, "failed", result.Output, result.Error);
    }

    private IAgent ResolveAgent(string name)
    {
        try { return _registry.Resolve(name); }
        catch (KeyNotFoundException) { throw new InvalidOperationException($"No runtime adapter for agent '{name}'."); }
    }

    private static List<OrchestrationPlanTask> ReadyTasks(OrchestrationPlanDocument plan)
    {
        var done = plan.Tasks.Where(t => string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id).ToHashSet();
        return plan.Tasks
            .Where(t => IsPending(t.Status) && t.DependsOn.All(d => done.Contains(d)))
            .ToList();
    }

    private static bool IsPending(string? status) =>
        string.IsNullOrWhiteSpace(status) || string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase);

    private static AgentTask ToAgentTask(OrchestrationPlanTask task, Guid tenantId)
    {
        var input = task.Input is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(task.Input, StringComparer.OrdinalIgnoreCase);
        input["tenant_id"] = tenantId.ToString("D");
        return new AgentTask(task.Id, task.Agent, task.Description, input);
    }

    private static string SerializeTaskInput(OrchestrationPlanTask task) =>
        System.Text.Json.JsonSerializer.Serialize(task.Input ?? new Dictionary<string, string>());
}
