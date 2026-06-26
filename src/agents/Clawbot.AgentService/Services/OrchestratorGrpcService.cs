using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.AgentService.Services;

public sealed partial class OrchestratorGrpcService(
    PlanningOrchestrator legacyOrchestrator,
    SemanticKernelPlanGenerator planGenerator,
    AutonomousOrchestrator autonomousOrchestrator,
    IAgentCatalog catalog,
    IEnumerable<IAgent> adapters,
    ILlmCallScope llmScope,
    IPiiRedactor redactor,
    OrchestratorCostGuard costGuard,
    AppDbContext db,
    IClock clock,
    ILogger<OrchestratorGrpcService> logger,
    IServiceScopeFactory? scopeFactory = null) : Orchestrator.OrchestratorBase
{
    private const int MaxConcurrency = 3;
    private const int MaxReplans = 2;
    private const string OrchestratorAgentCode = "orchestrator";

    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> RunningSessions = new();

    private readonly PlanningOrchestrator _legacy = legacyOrchestrator;
    private readonly SemanticKernelPlanGenerator _planGenerator = planGenerator;
    private readonly AutonomousOrchestrator _autonomous = autonomousOrchestrator;
    private readonly IAgentCatalog _catalog = catalog;
    private readonly IReadOnlyList<IAgent> _adapters = adapters.ToArray();
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly IPiiRedactor _redactor = redactor;
    private readonly OrchestratorCostGuard _costGuard = costGuard;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILogger<OrchestratorGrpcService> _logger = logger;
    private readonly IServiceScopeFactory? _scopeFactory = scopeFactory;

    // --- Legacy keyword planner (kept for compatibility) ---

    public override Task<PlanResponse> Plan(PlanRequest request, ServerCallContext context)
    {
        var plan = _legacy.Plan(request.TenantId, request.Goal);
        var response = new PlanResponse { SessionId = plan.SessionId };
        response.Tasks.AddRange(plan.Tasks.Select(t => new PlannedTask
        {
            Id = t.Id,
            Agent = t.AgentName,
            Description = t.Description,
        }));
        return Task.FromResult(response);
    }

    public override async Task Trace(TraceRequest request, IServerStreamWriter<TraceEvent> responseStream, ServerCallContext context)
    {
        var tenantId = ParseTenant(request.TenantId);
        if (Guid.TryParse(request.SessionId, out var sessionId) && sessionId != Guid.Empty)
        {
            var sessionExists = await _db.AgentSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(session => session.Id == sessionId && session.TenantId == tenantId, context.CancellationToken)
                .ConfigureAwait(false);
            if (sessionExists)
            {
                var traces = await _db.AgentTraces
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(trace => _db.AgentSessions
                        .IgnoreQueryFilters()
                        .Any(session => session.Id == trace.SessionId && session.Id == sessionId && session.TenantId == tenantId))
                    .ToArrayAsync(context.CancellationToken)
                    .ConfigureAwait(false);

                if (traces.Length > 0)
                {
                    foreach (var trace in traces.OrderBy(trace => trace.OccurredAt))
                    {
                        await responseStream.WriteAsync(new TraceEvent
                        {
                            TaskId = trace.TaskId ?? string.Empty,
                            Phase = trace.Phase ?? string.Empty,
                            Message = trace.Message ?? string.Empty,
                            At = Timestamp.FromDateTime(trace.OccurredAt.UtcDateTime),
                            AgentName = trace.AgentName ?? string.Empty,
                        });
                    }
                    return;
                }
            }
        }

        var legacyTraces = _legacy.GetTrace(request.SessionId, request.TenantId);
        if (legacyTraces.Count == 0)
        {
            await responseStream.WriteAsync(new TraceEvent
            {
                Phase = "missing",
                Message = $"No orchestrator trace found for session {request.SessionId}.",
                At = Timestamp.FromDateTime(DateTime.UtcNow),
            });
            return;
        }

        foreach (var trace in legacyTraces)
        {
            await responseStream.WriteAsync(new TraceEvent
            {
                TaskId = trace.TaskId,
                Phase = trace.Phase,
                Message = trace.Message,
                At = Timestamp.FromDateTime(trace.At.UtcDateTime),
                AgentName = string.Empty,
            });
        }
    }

    // --- Dynamic orchestration lifecycle ---

    public override async Task<SessionResponse> Submit(SubmitRequest request, ServerCallContext context)
    {
        var tenantId = ParseTenant(request.TenantId);
        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "goal_required"));

        var ct = context.CancellationToken;
        var now = _clock.UtcNow;
        var redactedGoal = (await _redactor.RedactAsync(request.Goal, ct).ConfigureAwait(false)).RedactedText;

        // Persist a running placeholder BEFORE the planner runs, so a durable sessionId exists immediately.
        // The FE stores this id in the URL; planning progress + trace then survive navigation and F5, and
        // the planner call no longer races the HTTP request lifetime. Tie it to the orchestrator AgentConfig
        // so planning traces surface under that agent's "Sự kiện lỗi" tab on the dashboard.
        var orchestratorAgentId = await ResolveOrchestratorAgentIdAsync(tenantId, ct).ConfigureAwait(false);
        var session = AgentSession.Start(tenantId, orchestratorAgentId, conversationId: null, redactedGoal, now);
        session.AppendTrace(string.Empty, OrchestratorAgentCode, "planning_started", "Đang lập kế hoạch cho mục tiêu.", now);
        _db.AgentSessions.Add(session);
        await SaveAsync(ct).ConfigureAwait(false);

        // Production: plan + execute in the background, return the placeholder now so the UI polls progress.
        if (_scopeFactory is not null)
        {
            var sessionId = session.Id;
            var goal = request.Goal;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = ActivatorUtilities.CreateInstance<OrchestratorGrpcService>(scope.ServiceProvider);
                    await service.PlanAndRunPersistedAsync(sessionId, goal, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogBackgroundRunFailed(_logger, ex, sessionId);
                    await MarkBackgroundRunFailedAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                }
            }, CancellationToken.None);
            return ToResponse(session);
        }

        // No scope factory (tests / inline host): plan + execute synchronously within the request.
        var (costBlocked, costReason) = await PlanAndExecuteAsync(session, request.Goal, ct).ConfigureAwait(false);
        return ToResponse(session, costBlocked, costReason);
    }

    private async Task PlanAndRunPersistedAsync(Guid sessionId, string goal, CancellationToken ct)
    {
        var session = await _db.AgentSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null)
            return;

        await PlanAndExecuteAsync(session, goal, ct).ConfigureAwait(false);
    }

    // Generate and execute through V2. The session placeholder already exists, so progress survives F5.
    private async Task<(bool CostBlocked, string? CostReason)> PlanAndExecuteAsync(AgentSession session, string goal, CancellationToken ct)
    {
        var requireApproval = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == session.TenantId)
            .Select(t => t.RequireOrchestrationApproval)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var existingPlan = OrchestrationPlanJson.TryParse(session.PlanJson);
        var result = existingPlan?.Tasks is { Count: > 0 } && session.Status == AgentSessionStatuses.Running
            ? await _autonomous.RunExistingPlanAsync(new AutonomousRunRequest(session.TenantId, session.Id, goal, "manual", RequiresApproval: false), existingPlan, ct).ConfigureAwait(false)
            : await _autonomous.RunAsync(new AutonomousRunRequest(session.TenantId, session.Id, goal, "manual", requireApproval), ct).ConfigureAwait(false);

        return (result.Reason == "cost_cap_preflight", result.Reason);
    }

    public override async Task<SessionResponse> GetPlan(SessionRef request, ServerCallContext context)
    {
        var session = await LoadAsync(request.TenantId, request.SessionId, context.CancellationToken).ConfigureAwait(false);
        return ToResponse(session);
    }

    public override async Task<SessionResponse> UpdatePlan(UpdatePlanRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var session = await LoadAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false);
        EnsureEtagMatches(session, request.ExpectedEtag);

        var plan = OrchestrationPlanJson.TryParse(request.PlanJson)
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid_plan_json"));
        var catalogEntries = await _catalog.ListAsync(ct).ConfigureAwait(false);
        var validation = OrchestrationPlanValidator.Validate(plan, catalogEntries);
        if (!validation.IsValid)
            throw new RpcException(new Status(StatusCode.InvalidArgument, validation.Error ?? "invalid_plan"));

        try
        {
            var redactedPlan = await OrchestrationPlanRedactor.RedactAsync(plan, _redactor, ct).ConfigureAwait(false);
            session.UpdatePlan(OrchestrationPlanJson.Serialize(redactedPlan));
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        await SaveAsync(ct).ConfigureAwait(false);
        return ToResponse(session);
    }

    public override async Task<SessionResponse> Approve(SessionRef request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var session = await LoadAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false);
        EnsureEtagMatches(session, request.ExpectedEtag);

        try
        {
            session.Approve();
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        await SaveAsync(ct).ConfigureAwait(false);
        await StartExecutionAsync(session.Id, session.Goal ?? string.Empty, ct).ConfigureAwait(false);
        return ToResponse(session);
    }

    public override async Task<SessionResponse> Control(ControlRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var session = await LoadAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false);
        EnsureEtagMatches(session, request.ExpectedEtag);

        try
        {
            switch ((request.Action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "pause":
                    session.Pause();
                    CancelRunningSession(session.Id);
                    break;
                case "resume":
                    session.Resume();
                    break;
                case "cancel":
                    session.Cancel(_clock.UtcNow);
                    CancelRunningSession(session.Id);
                    break;
                default:
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "unknown_action"));
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        await SaveAsync(ct).ConfigureAwait(false);
        if (session.Status == AgentSessionStatuses.Running && string.Equals(request.Action, "resume", StringComparison.OrdinalIgnoreCase))
            await StartExecutionAsync(session.Id, session.Goal ?? string.Empty, ct).ConfigureAwait(false);
        return ToResponse(session);
    }

    // --- Execution ---

    private async Task StartExecutionAsync(Guid sessionId, string goal, CancellationToken requestCt)
    {
        if (_scopeFactory is null)
        {
            var session = await _db.AgentSessions
                .IgnoreQueryFilters()
                .FirstAsync(s => s.Id == sessionId, requestCt)
                .ConfigureAwait(false);
            await PlanAndExecuteAsync(session, goal, requestCt).ConfigureAwait(false);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = ActivatorUtilities.CreateInstance<OrchestratorGrpcService>(scope.ServiceProvider);
                await service.PlanAndRunPersistedAsync(sessionId, goal, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogBackgroundRunFailed(_logger, ex, sessionId);
                await MarkBackgroundRunFailedAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
            }
        }, CancellationToken.None);
    }

    private async Task MarkBackgroundRunFailedAsync(Guid sessionId, CancellationToken ct)
    {
        using var scope = _scopeFactory?.CreateScope();
        if (scope is null)
            return;

        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scopedClock = scope.ServiceProvider.GetRequiredService<IClock>();
        var session = await scopedDb.AgentSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null || session.Status is AgentSessionStatuses.Completed or AgentSessionStatuses.Failed or AgentSessionStatuses.Cancelled)
            return;

        session.Fail(scopedClock.UtcNow);
        await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task RunAsync(AgentSession session, string goal, CancellationToken ct)
    {
        var plan = OrchestrationPlanJson.TryParse(session.PlanJson);
        if (plan is null || plan.Tasks.Count == 0)
        {
            session.Finish(_clock.UtcNow);
            await SaveAsync(ct).ConfigureAwait(false);
            return;
        }

        var catalogEntries = await _catalog.ListAsync(ct).ConfigureAwait(false);
        foreach (var task in plan.Tasks)
        {
            session.AppendTrace(task.Id, task.Agent, "planned", task.Description, _clock.UtcNow);
        }
        await SaveAsync(ct).ConfigureAwait(false);

        var agents = OrchestrationAgents.Build(_adapters, catalogEntries);
        using var efBackedAgentGate = new SemaphoreSlim(1, 1);
        var guardedAgents = agents.ToDictionary(
            pair => pair.Key,
            pair => (IAgent)new RuntimeGuardedAgent(
                WrapEfBackedAgent(pair.Value, efBackedAgentGate),
                session.Id,
                session.TenantId,
                _db,
                _clock,
                _redactor,
                _costGuard,
                _llmScope,
                efBackedAgentGate),
            StringComparer.OrdinalIgnoreCase);

        ParallelDagExecutor.ReplanCallback replanner = async (current, failed, attempt, replanCt) =>
        {
            try
            {
                var replanGoal = OrchestrationReplan.BuildReplanGoal(goal, failed);
                OrchestrationPlanDocument regenerated;
                using (_llmScope.Begin(session.TenantId, OrchestratorAgentCode))
                {
                    regenerated = await _planGenerator.GenerateAsync(replanGoal, catalogEntries, replanCt).ConfigureAwait(false);
                }

                session.IncrementReplan();
                session.AppendTrace(
                    failed[0].Id,
                    failed[0].Agent,
                    "re-planned",
                    $"Re-plan attempt {attempt} after failure: {failed[0].Error}",
                    _clock.UtcNow);
                await SaveAsync(replanCt).ConfigureAwait(false);
                return OrchestrationReplan.Merge(current, regenerated, attempt);
            }
            catch (Exception ex)
            {
                LogReplanFailed(_logger, ex, session.Id);
                return null;
            }
        };

        var executor = new ParallelDagExecutor(guardedAgents, MaxConcurrency, replanner, MaxReplans,
            async (current, task, result, progressCt) =>
            {
                if (await ApplyExternalStopAsync(session, progressCt).ConfigureAwait(false))
                    return;

                session.RecordRun(OrchestrationPlanJson.Serialize(current));
                var status = current.Tasks.FirstOrDefault(t => t.Id == task.Id)?.Status ?? task.Status;
                session.AppendTrace(
                    task.Id,
                    task.Agent,
                    result.Success ? "completed" : status == "skipped" ? "skipped" : "failed",
                    result.Error ?? result.Output ?? string.Empty,
                    _clock.UtcNow);
                await SaveAsync(progressCt).ConfigureAwait(false);
            },
            async (task, progressCt) =>
            {
                if (await ApplyExternalStopAsync(session, progressCt).ConfigureAwait(false))
                    return;

                session.AppendTrace(task.Id, task.Agent, "started", task.Description, _clock.UtcNow);
                await SaveAsync(progressCt).ConfigureAwait(false);
            });

        OrchestrationPlanDocument final;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        RunningSessions[session.Id] = linkedCts;
        try
        {
            using (_llmScope.Begin(session.TenantId, OrchestratorAgentCode))
            {
                final = await executor.ExecuteAsync(plan, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (RunningSessions.TryRemove(session.Id, out var registeredCts))
                registeredCts.Dispose();
        }

        session.RecordRun(OrchestrationPlanJson.Serialize(final));
        if (session.Status is AgentSessionStatuses.Paused or AgentSessionStatuses.Cancelled)
        {
            await SaveAsync(ct).ConfigureAwait(false);
            return;
        }

        var anyFailed = final.Tasks.Any(task =>
            string.Equals(task.Status, "failed", StringComparison.OrdinalIgnoreCase));
        if (anyFailed)
            session.Fail(_clock.UtcNow);
        else
            session.Finish(_clock.UtcNow);

        await SaveAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> ApplyExternalStopAsync(AgentSession session, CancellationToken ct)
    {
        var latest = await _db.AgentSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == session.Id && s.TenantId == session.TenantId, ct)
            .ConfigureAwait(false);

        if (latest?.Status == AgentSessionStatuses.Cancelled)
        {
            if (session.Status != AgentSessionStatuses.Cancelled)
                session.Cancel(_clock.UtcNow);
            return true;
        }

        if (latest?.Status == AgentSessionStatuses.Paused)
        {
            if (session.Status == AgentSessionStatuses.Running)
                session.Pause();
            return true;
        }

        return false;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "etag_mismatch"), ex.Message);
        }
    }

    private async Task<AgentSession> LoadAsync(string tenantRaw, string sessionRaw, CancellationToken ct)
    {
        var tenantId = ParseTenant(tenantRaw);
        if (!Guid.TryParse(sessionRaw, out var sessionId) || sessionId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "session_id_required"));

        var session = await _db.AgentSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct).ConfigureAwait(false);

        return session ?? throw new RpcException(new Status(StatusCode.NotFound, "session_not_found"));
    }

    // Resolve the orchestrator AgentConfig id for the tenant so planning sessions/traces are attributed to
    // it on the dashboard. Null if not seeded — session simply stays unattributed (no failure).
    private async Task<Guid?> ResolveOrchestratorAgentIdAsync(Guid tenantId, CancellationToken ct) =>
        await _db.AgentConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Code == OrchestratorAgentCode)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    private static Guid ParseTenant(string raw)
    {
        if (!Guid.TryParse(raw, out var tenantId) || tenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        return tenantId;
    }

    private static void EnsureEtagMatches(AgentSession session, string? expectedEtag)
    {
        if (string.IsNullOrWhiteSpace(expectedEtag))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "etag_required"));

        if (!string.Equals(EncodeEtag(session), expectedEtag, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "etag_mismatch"));
    }

    private static string EncodeEtag(AgentSession session)
    {
        if (session.RowVersion is { Length: > 0 })
            return Convert.ToBase64String(session.RowVersion);

        var material = string.Join('|', session.Id, session.Status, session.PlanJson, session.ReplanCount, session.FinishedAt?.ToUnixTimeMilliseconds());
        return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material)));
    }

    private static SessionResponse ToResponse(AgentSession session, bool costBlocked = false, string? costReason = null)
    {
        var response = new SessionResponse
        {
            SessionId = session.Id.ToString("D"),
            Status = session.Status,
            RequiresApproval = session.RequiresApproval,
            Goal = session.Goal ?? string.Empty,
            CostBlocked = costBlocked,
            CostReason = costReason ?? string.Empty,
            ReplanCount = session.ReplanCount,
            Etag = EncodeEtag(session),
            PlanJson = session.PlanJson,
        };

        var plan = OrchestrationPlanJson.TryParse(session.PlanJson);
        if (plan?.Tasks is not null)
        {
            response.Tasks.AddRange(plan.Tasks.Select(task =>
            {
                var planned = new PlannedTask
                {
                    Id = task.Id,
                    Agent = task.Agent,
                    Description = task.Description,
                    Status = task.Status ?? string.Empty,
                    Output = task.Output ?? string.Empty,
                    Error = task.Error ?? string.Empty,
                    InputJson = SerializeInput(task.Input),
                };
                planned.DependsOn.AddRange(task.DependsOn);
                return planned;
            }));
        }

        return response;
    }

    private static string SerializeInput(IReadOnlyDictionary<string, string> input) =>
        System.Text.Json.JsonSerializer.Serialize(input, WebJsonOptions);

    private static IAgent WrapEfBackedAgent(IAgent agent, SemaphoreSlim gate) =>
        agent.Name is "lead-agent" or "report-agent"
            ? new SerializingAgent(agent, gate)
            : agent;

    private static void CancelRunningSession(Guid sessionId)
    {
        if (RunningSessions.TryGetValue(sessionId, out var cts))
            cts.Cancel();
    }

    private sealed class RuntimeGuardedAgent(
        IAgent inner,
        Guid sessionId,
        Guid tenantId,
        AppDbContext db,
        IClock clock,
        IPiiRedactor redactor,
        OrchestratorCostGuard costGuard,
        ILlmCallScope llmScope,
        SemaphoreSlim dbGate) : IAgent
    {
        private const decimal EstimatedUsd = SemanticKernelOrchestrator.PerTaskEstimateUsd;

        public string Name => inner.Name;

        public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
        {
            AgentSession? session;
            await dbGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                session = await db.AgentSessions
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct).ConfigureAwait(false);
            }
            finally
            {
                dbGate.Release();
            }
            if (session is null)
                return await RedactAsync(new AgentResult(task.Id, Success: false, Output: string.Empty, Error: "session_not_found"), ct).ConfigureAwait(false);
            if (session.Status == AgentSessionStatuses.Cancelled)
                return await RedactAsync(new AgentResult(task.Id, Success: false, Output: string.Empty, Error: "cancelled"), ct).ConfigureAwait(false);
            if (session.Status == AgentSessionStatuses.Paused)
                return await RedactAsync(new AgentResult(task.Id, Success: false, Output: string.Empty, Error: "paused"), ct).ConfigureAwait(false);

            var reservationAt = clock.UtcNow;
            var reserved = await costGuard.TryReserveAsync(tenantId, EstimatedUsd, reservationAt, ct).ConfigureAwait(false);
            if (!reserved.Allowed)
                return await RedactAsync(new AgentResult(task.Id, Success: false, Output: string.Empty, Error: reserved.Reason ?? "cost_cap_midrun"), ct).ConfigureAwait(false);

            AgentResult result;
            try
            {
                using var _costScope = llmScope.Begin(tenantId, inner.Name, reservationAt, reserved.ReservationId);
                result = await inner.ExecuteAsync(WithServerTenant(task), ct).ConfigureAwait(false);
            }
            catch
            {
                await costGuard.ReleaseReservationAsync(tenantId, reserved.ReservationId, ct).ConfigureAwait(false);
                throw;
            }

            return await RedactAsync(result, ct).ConfigureAwait(false);
        }

        private async Task<AgentResult> RedactAsync(AgentResult result, CancellationToken ct)
        {
            var output = string.IsNullOrEmpty(result.Output)
                ? result.Output
                : (await redactor.RedactAsync(result.Output, ct).ConfigureAwait(false)).RedactedText;
            var error = string.IsNullOrEmpty(result.Error)
                ? result.Error
                : (await redactor.RedactAsync(result.Error, ct).ConfigureAwait(false)).RedactedText;
            return result with { Output = output, Error = error };
        }

        private AgentTask WithServerTenant(AgentTask task)
        {
            var input = task.Input.ToDictionary(StringComparer.OrdinalIgnoreCase);
            input["tenant_id"] = tenantId.ToString("D");
            return task with { Input = input };
        }

        private static decimal? ExtractUsdCost(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(output);
                return TryFindDecimal(doc.RootElement, "usdCost")
                    ?? TryFindDecimal(doc.RootElement, "usd_cost")
                    ?? TryFindDecimal(doc.RootElement, "UsdCost");
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        private static decimal? TryFindDecimal(System.Text.Json.JsonElement element, string name)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object && element.TryGetProperty(name, out var prop) && prop.TryGetDecimal(out var value))
                return value;

            if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            foreach (var child in element.EnumerateObject())
            {
                var nested = TryFindDecimal(child.Value, name);
                if (nested.HasValue)
                    return nested;
            }

            return null;
        }
    }

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "Orchestration re-plan failed for session {SessionId}")]
    private static partial void LogReplanFailed(ILogger logger, Exception ex, Guid sessionId);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Error, Message = "Background orchestration run failed for session {SessionId}")]
    private static partial void LogBackgroundRunFailed(ILogger logger, Exception ex, Guid sessionId);
}
