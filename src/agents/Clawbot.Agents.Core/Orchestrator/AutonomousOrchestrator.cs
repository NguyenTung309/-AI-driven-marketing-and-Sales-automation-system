using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
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
    private readonly IRagRetriever _ragRetriever;
    private readonly IClaudeChatClient _chatClient;
    private readonly IClock _clock;
    private readonly ToolRegistry? _toolRegistry;
    private readonly IOrchestrationApprovalResolver? _approvalResolver;
    private readonly AutonomousOrchestratorOptions _options;
    // SPEC-16 P4-4/P4-2: per-run flags set at the start of ExecutePlanAsync.
    private bool _requireHighRiskApproval;
    private bool _dryRun;

    private const string OrchestratorAgentCode = "orchestrator";

    public AutonomousOrchestrator(
        IAutonomousPlanner planner,
        IAgentDefinitionCatalog catalog,
        AgentRegistry registry,
        IA2AMailbox mailbox,
        OrchestratorCostGuard costGuard,
        ILlmCallScope llmScope,
        IAutonomousRunSink sink,
        IRagRetriever ragRetriever,
        IClaudeChatClient chatClient,
        IClock clock,
        AutonomousOrchestratorOptions? options = null,
        ToolRegistry? toolRegistry = null,
        IOrchestrationApprovalResolver? approvalResolver = null)
    {
        _planner = planner;
        _catalog = catalog;
        _registry = registry;
        _mailbox = mailbox;
        _costGuard = costGuard;
        _llmScope = llmScope;
        _sink = sink;
        _ragRetriever = ragRetriever;
        _chatClient = chatClient;
        _clock = clock;
        _options = options ?? new AutonomousOrchestratorOptions();
        _toolRegistry = toolRegistry;
        _approvalResolver = approvalResolver;
    }

    public async Task<AutonomousRunResult> RunAsync(AutonomousRunRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            return AutonomousRunResult.Failed("goal_required", 0);

        var entries = await _catalog.ListAsync(request.TenantId, ct).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            // Surface the reason in the trace — otherwise the FE hangs on "planning_started" with no clue.
            await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "planning_failed",
                "Chưa có agent điều phối nào (agent_definitions). Seed sub-agent cho tenant trước khi lập kế hoạch.", _clock.UtcNow, ct).ConfigureAwait(false);
            await _sink.FailAsync(request.TenantId, request.SessionId, "no_agents", _clock.UtcNow, ct).ConfigureAwait(false);
            return AutonomousRunResult.Failed("no_agents", 0);
        }

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
            await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "planning_failed", ex.Message, _clock.UtcNow, ct).ConfigureAwait(false);
            await _sink.FailAsync(request.TenantId, request.SessionId, "plan_failed", _clock.UtcNow, ct).ConfigureAwait(false);
            return AutonomousRunResult.Failed("plan_failed", 0);
        }

        await _sink.PersistPlanAsync(request.TenantId, request.SessionId, plan, request.RequiresApproval, ct).ConfigureAwait(false);
        // SPEC-16 P3-7: post a human-readable plan summary trace right after planning so the user can read the DAG,
        // not just a raw JSON plan blob.
        await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "plan_summary",
            BuildPlanSummary(plan), _clock.UtcNow, ct).ConfigureAwait(false);
        await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "planning_completed", $"Planned {plan.Tasks.Count} task(s).", _clock.UtcNow, ct).ConfigureAwait(false);
        if (request.RequiresApproval)
            return AutonomousRunResult.PendingApproval(0);

        return await ExecutePlanAsync(request, plan, entries, ct).ConfigureAwait(false);
    }

    public async Task<AutonomousRunResult> RunExistingPlanAsync(AutonomousRunRequest request, OrchestrationPlanDocument plan, CancellationToken ct = default)
    {
        var entries = await _catalog.ListAsync(request.TenantId, ct).ConfigureAwait(false);
        return await ExecutePlanAsync(request, plan, entries, ct).ConfigureAwait(false);
    }

    private async Task<AutonomousRunResult> ExecutePlanAsync(
        AutonomousRunRequest request,
        OrchestrationPlanDocument plan,
        IReadOnlyList<AgentDefinitionCatalogEntry> entries,
        CancellationToken ct)
    {
        var preflight = await _costGuard.CanStartAsync(request.TenantId, plan.Tasks.Count * _options.PerTaskEstimateUsd, _clock.UtcNow, ct).ConfigureAwait(false);
        if (!preflight.Allowed)
        {
            await _sink.FailAsync(request.TenantId, request.SessionId, preflight.Reason ?? "cost_cap_preflight", _clock.UtcNow, ct).ConfigureAwait(false);
            return AutonomousRunResult.Failed(preflight.Reason ?? "cost_cap_preflight", 0);
        }

        var byCode = BuildDefinitionLookup(entries);
        // SPEC-16 P4-4: load the tenant's high-risk approval toggle once per run; the worker refuses High-risk tools when on.
        _requireHighRiskApproval = _approvalResolver is not null
            && await _approvalResolver.IsRequiredAsync(request.TenantId, ct).ConfigureAwait(false);
        // SPEC-16 P4-2: capture the run's dry-run flag so the worker previews tool actions without side effects.
        _dryRun = request.DryRun;

        // Execution proceeds in waves: each wave runs every currently-ready task. A wave that produces no
        // failures simply advances the DAG and is NOT charged against the replan budget — so a deep but healthy
        // chain (research→content→reviewer→publisher→reporter) completes regardless of its depth. _options.MaxRounds
        // bounds only REPLANS (recovery after a failed task). Previously the wave loop itself was capped at MaxRounds,
        // so any chain deeper than MaxRounds tripped a false max_rounds even with zero failures — the real root cause.
        var replans = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var hasFailed = plan.Tasks.Any(t => IsFailed(t.Status));
            var pending = plan.Tasks.Where(t => IsPending(t.Status)).ToArray();
            if (pending.Length == 0 && !hasFailed)
            {
                await EmitRunSummaryAsync(request, plan, ct).ConfigureAwait(false);
                await _sink.CompleteAsync(request.TenantId, request.SessionId, _clock.UtcNow, ct).ConfigureAwait(false);
                return AutonomousRunResult.Completed(replans);
            }

            var ready = ReadyTasks(plan);
            if (ready.Count > 0)
            {
                foreach (var task in ready)
                {
                    ct.ThrowIfCancellationRequested();
                    plan = await ExecuteTaskAsync(request, plan, task, byCode, ct).ConfigureAwait(false);
                    await _sink.PersistPlanAsync(request.TenantId, request.SessionId, plan, ct: ct).ConfigureAwait(false);
                    if (await _sink.IsStoppedAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false))
                        return AutonomousRunResult.Failed("stopped", replans);
                }

                // Healthy wave → advance to the next wave without consuming the replan budget.
                if (!plan.Tasks.Any(t => IsFailed(t.Status)))
                    continue;
            }

            var failed = plan.Tasks.Where(t => IsFailed(t.Status)).ToArray();
            if (failed.Length == 0)
            {
                // Nothing ready, nothing failed, yet work remains → unsatisfiable dependencies.
                await _sink.FailAsync(request.TenantId, request.SessionId, "dependency_blocked", _clock.UtcNow, ct).ConfigureAwait(false);
                return AutonomousRunResult.Failed("dependency_blocked", replans);
            }

            if (replans >= _options.MaxRounds)
            {
                await _sink.FailAsync(request.TenantId, request.SessionId, "max_rounds", _clock.UtcNow, ct).ConfigureAwait(false);
                return AutonomousRunResult.Failed("max_rounds", replans);
            }

            replans++;
            await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "re-planned", $"Re-planning after {failed.Length} failed task(s).", _clock.UtcNow, ct).ConfigureAwait(false);
            using (_llmScope.Begin(request.TenantId, OrchestratorAgentCode))
            {
                plan = await _planner.ReplanAsync(request.TenantId, request.Goal, entries.Select(e => e.ToPlannerEntry()).ToArray(), failed, ct).ConfigureAwait(false);
            }
            await _sink.PersistPlanAsync(request.TenantId, request.SessionId, plan, ct: ct).ConfigureAwait(false);
        }
    }

    private async Task<OrchestrationPlanDocument> ExecuteTaskAsync(
        AutonomousRunRequest request,
        OrchestrationPlanDocument plan,
        OrchestrationPlanTask task,
        Dictionary<string, AgentDefinitionCatalogEntry> byCode,
        CancellationToken ct)
    {
        byCode.TryGetValue(task.Agent, out var definition);

        var reservation = await _costGuard.TryReserveAsync(request.TenantId, _options.PerTaskEstimateUsd, _clock.UtcNow, ct).ConfigureAwait(false);
        if (!reservation.Allowed)
        {
            await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "failed", reservation.Reason ?? "cost_cap_midrun", _clock.UtcNow, ct).ConfigureAwait(false);
            return plan.WithTaskStatus(task.Id, "failed", null, reservation.Reason ?? "cost_cap_midrun");
        }

        if (definition is not null)
            await _mailbox.SendAsync(request.TenantId, request.SessionId, null, definition.Id, task.Id, "delegate", SerializeTaskInput(task), ct).ConfigureAwait(false);
        await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "started", task.Description, _clock.UtcNow, ct).ConfigureAwait(false);

        AgentResult result;
        try
        {
            using var _costScope = _llmScope.Begin(request.TenantId, task.Agent, _clock.UtcNow, reservation.ReservationId);
            var agent = ResolveAgent(task.Agent, definition, request, task);
            // EARS[WHEN a delegated task suffers a transient LLM/HTTP failure THE SYSTEM SHALL retry the same task
            // with backoff (up to MaxTransientRetries) without burning a replan round, so a slow completion no longer
            // cascades into max_rounds]
            result = await ExecuteAgentWithTransientRetryAsync(
                agent, ToAgentTask(task, plan, request.TenantId), request, task, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

    // SPEC-16 P2-11: post a human-readable run summary composed from the structured sub-agent results, so the
    // orchestrator reports back to the user in prose, not just per-task traces.
    private async Task EmitRunSummaryAsync(AutonomousRunRequest request, OrchestrationPlanDocument plan, CancellationToken ct)
    {
        var summary = BuildRunSummary(request, plan);
        await _sink.TraceAsync(request.TenantId, request.SessionId, string.Empty, OrchestratorAgentCode, "run_summary",
            summary, _clock.UtcNow, ct).ConfigureAwait(false);
    }

    private static string BuildRunSummary(AutonomousRunRequest request, OrchestrationPlanDocument plan)
    {
        var completed = plan.Tasks.Count(t => string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase));
        var failed = plan.Tasks.Count(t => string.Equals(t.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var sb = new System.Text.StringBuilder(256);
        sb.Append("Hoàn thành ").Append(completed).Append('/').Append(plan.Tasks.Count).Append(" công việc");
        if (failed > 0) sb.Append(" (").Append(failed).Append(" thất bại)");
        sb.Append(" cho mục tiêu: ").Append(request.Goal).Append('.');
        foreach (var task in plan.Tasks)
        {
            sb.Append(' ').Append('[').Append(task.Agent).Append("] ").Append(task.Description);
            if (string.Equals(task.Status, "completed", StringComparison.OrdinalIgnoreCase))
                sb.Append(" — xong");
            else if (string.Equals(task.Status, "failed", StringComparison.OrdinalIgnoreCase))
                sb.Append(" — lỗi").Append(task.Error is null ? string.Empty : ": " + task.Error);
        }
        // ponytail: cap the summary length so a large DAG does not flood the trace; downstream can fetch full output.
        return sb.Length > 1200 ? sb.ToString(0, 1197) + "..." : sb.ToString();
    }

    private static string BuildPlanSummary(OrchestrationPlanDocument plan)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append("Kế hoạch ").Append(plan.Tasks.Count).Append(" bước:");
        foreach (var task in plan.Tasks)
            sb.Append(' ').Append(task.Agent).Append(':').Append(task.Description).Append(';');
        return sb.Length > 1200 ? sb.ToString(0, 1197) + "..." : sb.ToString();
    }

    private async Task<AgentResult> ExecuteAgentWithTransientRetryAsync(
        IAgent agent, AgentTask agentTask, AutonomousRunRequest request, OrchestrationPlanTask task, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await agent.ExecuteAsync(agentTask, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                attempt++;
                if (attempt > _options.MaxTransientRetries)
                    throw; // retries exhausted -> bubbles up as a failed task (replan round), no longer transient
                var delayMs = _options.TransientBackoffBaseMs * attempt;
                await _sink.TraceAsync(request.TenantId, request.SessionId, task.Id, task.Agent, "transient_retry",
                    $"Transient failure ({ex.GetType().Name}: {ex.Message}). Retry {attempt}/{_options.MaxTransientRetries} after {delayMs}ms.",
                    _clock.UtcNow, ct).ConfigureAwait(false);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
    }

    // Transient = timeout-induced cancellation (HttpClient.Timeout throws TaskCanceledException when the user ct is NOT the cause),
    // explicit TimeoutException, or 5xx/429 HTTP. Logical failures fall through to the replan path.
    private static bool IsTransient(Exception ex) =>
        ex is TimeoutException
        || ex is OperationCanceledException
        || (ex is System.Net.Http.HttpRequestException hre && IsTransientHttpStatus(hre));

    private static bool IsTransientHttpStatus(System.Net.Http.HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests
        || (int?)ex.StatusCode >= 500;

    // EARS[WHEN a data-defined agent declares allowed tools THE SYSTEM SHALL run it through the tool-capable
    // ReAct worker so it can delegate to the registered adapters (the "hands") instead of downgrading to text-only;
    // WHEN no definition exists THE SYSTEM SHALL fall back to the static runtime registry adapter]
    private IAgent ResolveAgent(string name, AgentDefinitionCatalogEntry? definition, AutonomousRunRequest request, OrchestrationPlanTask task)
    {
        if (definition is not null)
            return new GenericLlmAgentWorker(definition, _ragRetriever, _chatClient, _costGuard, _llmScope, _toolRegistry,
                _requireHighRiskApproval, _sink, _clock, new WorkerRunContext(request.TenantId, request.SessionId), _dryRun);

        try { return _registry.Resolve(name); }
        catch (KeyNotFoundException) { throw new InvalidOperationException($"No runtime adapter for agent '{name}'."); }
    }

    private static Dictionary<string, AgentDefinitionCatalogEntry> BuildDefinitionLookup(IReadOnlyList<AgentDefinitionCatalogEntry> entries)
    {
        var lookup = new Dictionary<string, AgentDefinitionCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            Add(entry.Code, entry);
            Add(entry.ShortName, entry);
            Add(entry.AgentType, entry);
        }
        return lookup;

        void Add(string? key, AgentDefinitionCatalogEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(key))
                lookup.TryAdd(key, entry);
        }
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

    private static bool IsFailed(string? status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

    private static AgentTask ToAgentTask(OrchestrationPlanTask task, OrchestrationPlanDocument plan, Guid tenantId)
    {
        var input = task.Input is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(task.Input, StringComparer.OrdinalIgnoreCase);
        input["tenant_id"] = tenantId.ToString("D");

        // Thread completed predecessor outputs into the input so a dependent agent (reviewer, publisher, …) actually
        // receives upstream results, not just its static planned input. Without this, DependsOn only orders execution
        // and passes no data — the downstream agent soft-fails ("thiếu nội dung"/"thiếu content_id") and the
        // orchestrator re-plans until it exhausts MaxRounds (max_rounds).
        if (task.DependsOn.Count > 0)
        {
            var upstream = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in task.DependsOn)
            {
                var src = plan.Tasks.FirstOrDefault(t => string.Equals(t.Id, dep, StringComparison.OrdinalIgnoreCase));
                if (src is null || string.IsNullOrEmpty(src.Output)) continue;
                var key = upstream.ContainsKey(src.Agent) ? $"{src.Agent}:{src.Id}" : src.Agent;
                upstream[key] = src.Output;
            }
            if (upstream.Count > 0)
                input["upstream_results"] = System.Text.Json.JsonSerializer.Serialize(upstream);
        }

        return new AgentTask(task.Id, task.Agent, task.Description, input);
    }

    private static string SerializeTaskInput(OrchestrationPlanTask task) =>
        System.Text.Json.JsonSerializer.Serialize(task.Input ?? new Dictionary<string, string>());
}
