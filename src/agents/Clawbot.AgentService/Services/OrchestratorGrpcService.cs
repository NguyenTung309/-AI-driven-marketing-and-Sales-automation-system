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
    SemanticKernelPlanGenerator planGenerator,
    IAutonomousOrchestrator autonomousOrchestrator,
    IAgentCatalog catalog,
    IEnumerable<IAgent> adapters,
    ILlmCallScope llmScope,
    IPiiRedactor redactor,
    OrchestratorCostGuard costGuard,
    AgentScheduleRunner scheduleRunner,
    IOrchestratorCallerAuthorizer callerAuthorizer,
    IAutonomousRunSink runSink,
    AppDbContext db,
    IClock clock,
    ILogger<OrchestratorGrpcService> logger,
    IServiceScopeFactory? scopeFactory = null) : Orchestrator.OrchestratorBase
{
    private const int MaxConcurrency = 3;
    private const int MaxReplans = 2;
    private const string OrchestratorAgentCode = "orchestrator";
    // Cùng ngưỡng với OrchestrationPlanValidator.MaxTaskInputChars: output người sửa cũng nằm trong plan JSON.
    private const int MaxInterveneOutputChars = OrchestrationPlanValidator.MaxTaskInputChars;

    private static readonly System.Text.Json.JsonSerializerOptions WebJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> RunningSessions = new();

    private readonly SemanticKernelPlanGenerator _planGenerator = planGenerator;
    private readonly IAutonomousOrchestrator _autonomous = autonomousOrchestrator;
    private readonly IAgentCatalog _catalog = catalog;
    private readonly IReadOnlyList<IAgent> _adapters = adapters.ToArray();
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly IPiiRedactor _redactor = redactor;
    private readonly OrchestratorCostGuard _costGuard = costGuard;
    private readonly AgentScheduleRunner _scheduleRunner = scheduleRunner;
    private readonly IOrchestratorCallerAuthorizer _callerAuthorizer = callerAuthorizer;
    private readonly IAutonomousRunSink _runSink = runSink;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILogger<OrchestratorGrpcService> _logger = logger;
    private readonly IServiceScopeFactory? _scopeFactory = scopeFactory;

    // --- Dynamic orchestration lifecycle ---

    public override async Task<SessionResponse> Submit(SubmitRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "goal_required"));

        var ct = context.CancellationToken;
        var caller = await _callerAuthorizer.AuthorizeAsync(
                context,
                request.TenantId,
                request.UserId,
                "orchestration:run",
                ct)
            .ConfigureAwait(false);
        var tenantId = caller.TenantId;
        var now = _clock.UtcNow;
        var redactedGoal = (await _redactor.RedactAsync(request.Goal, ct).ConfigureAwait(false)).RedactedText;

        // Persist a running placeholder BEFORE the planner runs, so a durable sessionId exists immediately.
        // The FE stores this id in the URL; planning progress + trace then survive navigation and F5, and
        // the planner call no longer races the HTTP request lifetime. Tie it to the orchestrator AgentConfig
        // so planning traces surface under that agent's "Sự kiện lỗi" tab on the dashboard.
        var orchestratorAgentId = await ResolveOrchestratorAgentIdAsync(tenantId, ct).ConfigureAwait(false);
        // Authenticated service identity and the payload must agree; the payload is never authoritative.
        var session = AgentSession.Start(tenantId, orchestratorAgentId, conversationId: null, redactedGoal, now, userId: caller.UserId);
        session.AppendTrace(string.Empty, OrchestratorAgentCode, "planning_started", "Đang lập kế hoạch cho mục tiêu.", now);
        if (request.DryRun)
            session.AppendTrace(string.Empty, OrchestratorAgentCode, "dry_run", "Bản chạy thử — công cụ chỉ mô phỏng hành động, không thực thi thật.", now);
        _db.AgentSessions.Add(session);
        await SaveAsync(ct).ConfigureAwait(false);

        // Production: plan + execute in the background, return the placeholder now so the UI polls progress.
        if (_scopeFactory is not null)
        {
            var sessionId = session.Id;
            var goal = request.Goal;
            var dryRun = request.DryRun;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = ActivatorUtilities.CreateInstance<OrchestratorGrpcService>(scope.ServiceProvider);
                    await service.PlanAndRunPersistedAsync(sessionId, goal, dryRun, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OrchestrationPlanGenerationMismatchException or DbUpdateConcurrencyException)
                {
                    LogBackgroundRunFailed(_logger, ex, sessionId);
                }
                catch (Exception ex)
                {
                    LogBackgroundRunFailed(_logger, ex, sessionId);
                }
            }, CancellationToken.None);
            return ToResponse(session);
        }

        // No scope factory (tests / inline host): plan + execute synchronously within the request.
        var (costBlocked, costReason) = await PlanAndExecuteAsync(session, request.Goal, request.DryRun, ct).ConfigureAwait(false);
        return ToResponse(session, costBlocked, costReason);
    }

    public override async Task<RunScheduleResponse> RunSchedule(RunScheduleRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var caller = await _callerAuthorizer.AuthorizeAsync(
                context,
                request.TenantId,
                request.UserId,
                "orchestration:run",
                ct)
            .ConfigureAwait(false);
        if (!Guid.TryParse(request.ScheduleId, out var scheduleId) || scheduleId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "schedule_id_required"));

        var result = await _scheduleRunner.RunNowAsync(
            caller.TenantId,
            scheduleId,
            caller.UserId,
            ct).ConfigureAwait(false);
        var response = new RunScheduleResponse
        {
            Status = result.Status,
            RunId = result.RunId?.ToString("D") ?? string.Empty,
            SessionId = result.SessionId?.ToString("D") ?? string.Empty,
        };
        if (result.NextRunAt is { } nextRunAt)
            response.NextRunAt = Timestamp.FromDateTimeOffset(nextRunAt);
        if (result.LastRunAt is { } lastRunAt)
            response.LastRunAt = Timestamp.FromDateTimeOffset(lastRunAt);
        return response;
    }

    private async Task PlanAndRunPersistedAsync(Guid sessionId, string goal, bool dryRun, CancellationToken ct)
    {
        var session = await _db.AgentSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null)
            return;

        try
        {
            await PlanAndExecuteAsync(session, goal, dryRun, ct).ConfigureAwait(false);
        }
        catch (OrchestrationPlanGenerationMismatchException)
        {
            return;
        }
        catch (OrchestrationSessionNotRunningException)
        {
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogBackgroundRunFailed(_logger, ex, sessionId);
            await MarkBackgroundRunFailedAsync(
                session.TenantId,
                sessionId,
                session.ReplanCount,
                ex,
                ct).ConfigureAwait(false);
        }
    }

    // Generate and execute through V2. The session placeholder already exists, so progress survives F5.
    private async Task<IReadOnlySet<string>> ResolveExecutionPermissionsAsync(AgentSession session, CancellationToken ct)
    {
        if (session.ExecutionUserId is not { } userId)
            return new HashSet<string>(StringComparer.Ordinal);

        try
        {
            return await _callerAuthorizer.ResolvePermissionsAsync(session.TenantId, userId, ct)
                .ConfigureAwait(false);
        }
        catch (RpcException)
        {
            // A disabled/deprovisioned initiator must fail closed instead of retaining stale grants.
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private async Task<(bool CostBlocked, string? CostReason)> PlanAndExecuteAsync(AgentSession session, string goal, bool dryRun, CancellationToken ct)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!RunningSessions.TryAdd(session.Id, runCts))
            return (false, "run_already_active");

        try
        {
            var runCt = runCts.Token;
            var requireApproval = await _db.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Id == session.TenantId)
                .Select(t => t.RequireOrchestrationApproval)
                .FirstOrDefaultAsync(runCt).ConfigureAwait(false);

            var executionPermissions = await ResolveExecutionPermissionsAsync(session, runCt).ConfigureAwait(false);
            if (session.ExecutionUserId is null || !executionPermissions.Contains("orchestration:run"))
            {
                await _runSink.FailAndRejectOrphanedContentAsync(
                        session.TenantId,
                        session.Id,
                        "orchestration_execution_permission_denied",
                        session.ReplanCount,
                        _clock.UtcNow,
                        runCt)
                    .ConfigureAwait(false);
                return (false, "orchestration_execution_permission_denied");
            }

            var existingPlan = OrchestrationPlanJson.TryParse(session.PlanJson);
            var result = existingPlan?.Tasks is { Count: > 0 } && session.Status == AgentSessionStatuses.Running
                ? await _autonomous.RunExistingPlanAsync(new AutonomousRunRequest(session.TenantId, session.Id, goal, "manual", RequiresApproval: false, DryRun: dryRun, ExecutionPermissions: executionPermissions), existingPlan, runCt).ConfigureAwait(false)
                : await _autonomous.RunAsync(new AutonomousRunRequest(session.TenantId, session.Id, goal, "manual", requireApproval, DryRun: dryRun, ExecutionPermissions: executionPermissions), runCt).ConfigureAwait(false);

            return (result.Reason == "cost_cap_preflight", result.Reason);
        }
        finally
        {
            _ = RunningSessions.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(session.Id, runCts));
        }
    }

    public override async Task<SessionResponse> GetPlan(SessionRef request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        await _callerAuthorizer.AuthorizeAsync(context, request.TenantId, null, "orchestration:view", ct)
            .ConfigureAwait(false);
        var session = await LoadAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false);
        return ToResponse(session);
    }

    public override async Task<SessionResponse> UpdatePlan(UpdatePlanRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var caller = await _callerAuthorizer.AuthorizeAsync(
                context,
                request.TenantId,
                null,
                "orchestration:run",
                ct)
            .ConfigureAwait(false);
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
            session.UpdatePlan(OrchestrationPlanJson.Serialize(redactedPlan), caller.UserId);
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
        var caller = await _callerAuthorizer.AuthorizeAsync(
                context,
                request.TenantId,
                null,
                "orchestration:approve",
                ct)
            .ConfigureAwait(false);
        RequireExecutionPermission(caller);
        var session = await LoadAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false);
        EnsureEtagMatches(session, request.ExpectedEtag);

        try
        {
            session.Approve(caller.UserId);
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
        var caller = await _callerAuthorizer.AuthorizeAsync(
                context,
                request.TenantId,
                null,
                "orchestration:manage",
                ct)
            .ConfigureAwait(false);
        var session = await LoadAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false);
        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action != "cancel")
        {
            EnsureEtagMatches(session, request.ExpectedEtag);
        }

        try
        {
            switch (action)
            {
                case "pause":
                    session.RequestPause();
                    break;
                case "resume":
                    RequireExecutionPermission(caller);
                    if (RunningSessions.ContainsKey(session.Id))
                        throw new RpcException(new Status(StatusCode.FailedPrecondition, "pause_in_progress"));
                    session.Resume(caller.UserId);
                    break;
                case "cancel":
                    if (session.Status == AgentSessionStatuses.Cancelled)
                    {
                        return ToResponse(session);
                    }
                    await _runSink.CancelAsync(
                        session.TenantId,
                        session.Id,
                        session.ReplanCount,
                        null,
                        _clock.UtcNow,
                        ct).ConfigureAwait(false);
                    await _db.Entry(session).ReloadAsync(ct).ConfigureAwait(false);
                    CancelRunningSession(session.Id);
                    break;
                default:
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "unknown_action"));
            }
        }
        catch (OrchestrationSessionEtagMismatchException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "etag_mismatch"));
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

    private static void RequireExecutionPermission(OrchestratorCaller caller)
    {
        if (!caller.Permissions.Contains("orchestration:run"))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "execution_permission_denied"));
    }

    // Can thiệp thủ công vào MỘT task của phiên đang tạm dừng: sửa output, chạy lại, hoặc bỏ qua.
    // Không gọi planner => không tốn LLM. Đây là đường thay thế cho auto-replan (vốn sinh plan mới
    // hoàn toàn và chạy lại cả những bước đã xong).
    public override async Task<SessionResponse> InterveneTask(InterveneTaskRequest request, ServerCallContext context)
    {
        var ct = context.CancellationToken;
        var caller = await _callerAuthorizer.AuthorizeAsync(context, request.TenantId, request.UserId, "orchestration:manage", ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.TaskId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "task_id_required"));

        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action is not ("edit_output" or "retry" or "skip"))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "unknown_action"));
        if (action == "edit_output" && string.IsNullOrWhiteSpace(request.Output))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "output_required"));
        if ((request.Output ?? string.Empty).Length > MaxInterveneOutputChars)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "output_too_large"));

        var session = await LoadAsync(request.TenantId, request.SessionId, ct).ConfigureAwait(false);
        EnsureEtagMatches(session, request.ExpectedEtag);

        // Runner còn sống giữ bản plan trong RAM và sẽ ghi đè bản sửa ở lần PersistPlan kế tiếp.
        // Cùng lá chắn mà "resume" đang dùng.
        if (RunningSessions.ContainsKey(session.Id))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "run_in_progress"));
        if (session.Status != AgentSessionStatuses.Paused)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "session_not_paused"));

        var plan = OrchestrationPlanJson.TryParse(session.PlanJson)
            ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, "plan_not_available"));
        var target = plan.Tasks.FirstOrDefault(t => string.Equals(t.Id, request.TaskId, StringComparison.OrdinalIgnoreCase))
            ?? throw new RpcException(new Status(StatusCode.NotFound, "task_not_found"));

        // Người dùng có thể dán dữ liệu khách vào ô sửa — redact trước khi ghi, như mọi text dẫn xuất khác.
        var redactedOutput = action == "edit_output"
            ? (await _redactor.RedactAsync(request.Output ?? string.Empty, ct).ConfigureAwait(false)).RedactedText
            : null;

        var next = action switch
        {
            "edit_output" => plan.WithTaskStatus(target.Id, "completed", redactedOutput, null),
            "retry" => plan.WithTaskStatus(target.Id, "pending", null, null),
            _ => plan.WithTaskStatus(target.Id, "skipped", null, null),
        };

        // Sửa output của một bước đã xong sẽ vô nghĩa nếu các bước sau đã chạy với kết quả cũ:
        // chúng không đọc lại upstream. Reset để kết quả mới thực sự chảy xuống.
        var resetCount = 0;
        if (request.RerunDownstream)
            (next, resetCount) = ResetDownstream(next, target.Id);

        var catalogEntries = await _catalog.ListAsync(ct).ConfigureAwait(false);
        var validation = OrchestrationPlanValidator.Validate(next, catalogEntries);
        if (!validation.IsValid)
            throw new RpcException(new Status(StatusCode.InvalidArgument, validation.Error ?? "invalid_plan"));

        var (phase, message) = action switch
        {
            "edit_output" => ("task_edited", $"Người dùng đã sửa kết quả bước {target.Id}."),
            "retry" => ("task_retry", $"Người dùng yêu cầu chạy lại bước {target.Id}."),
            _ => ("task_skipped", $"Người dùng bỏ qua bước {target.Id}."),
        };
        if (resetCount > 0)
            message += $" Đặt lại {resetCount} bước phía sau để chạy lại.";

        try
        {
            session.UpdatePlan(OrchestrationPlanJson.Serialize(next), caller.UserId);
            session.AppendTrace(target.Id, OrchestratorAgentCode, phase, message, _clock.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        await SaveAsync(ct).ConfigureAwait(false);
        return ToResponse(session);
    }

    // Duyệt xuôi đồ thị phụ thuộc: mọi task phụ thuộc (trực tiếp lẫn gián tiếp) vào rootId mà đã chạy
    // xong/lỗi/bỏ qua đều quay về pending để chạy lại với dữ liệu mới.
    private static (OrchestrationPlanDocument Plan, int ResetCount) ResetDownstream(OrchestrationPlanDocument plan, string rootId)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        bool grew;
        do
        {
            grew = false;
            foreach (var task in plan.Tasks)
            {
                if (affected.Contains(task.Id)) continue;
                if (task.DependsOn.Any(affected.Contains) && affected.Add(task.Id))
                    grew = true;
            }
        }
        while (grew);

        var next = plan;
        var reset = 0;
        foreach (var task in plan.Tasks)
        {
            if (string.Equals(task.Id, rootId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!affected.Contains(task.Id)) continue;
            if (string.IsNullOrWhiteSpace(task.Status) || string.Equals(task.Status, "pending", StringComparison.OrdinalIgnoreCase)) continue;

            next = next.WithTaskStatus(task.Id, "pending", null, null);
            reset++;
        }

        return (next, reset);
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
            // Approval/resume path always executes for real — dry-run stops at the preview stage.
            await PlanAndExecuteAsync(session, goal, dryRun: false, requestCt).ConfigureAwait(false);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = ActivatorUtilities.CreateInstance<OrchestratorGrpcService>(scope.ServiceProvider);
                await service.PlanAndRunPersistedAsync(sessionId, goal, dryRun: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogBackgroundRunFailed(_logger, ex, sessionId);
            }
        }, CancellationToken.None);
    }

    // A background error must not bypass generation-scoped orphan cleanup. The generation captured when this
    // runner started is passed into the sink, so a stale runner cannot fail a newer durable plan.
    private async Task MarkBackgroundRunFailedAsync(
        Guid tenantId,
        Guid sessionId,
        int expectedGeneration,
        Exception? error,
        CancellationToken ct)
    {
        using var scope = _scopeFactory?.CreateScope();
        if (scope is null)
            return;

        var scopedClock = scope.ServiceProvider.GetRequiredService<IClock>();
        var sink = scope.ServiceProvider.GetRequiredService<IAutonomousRunSink>();
        var reason = error switch
        {
            PlanGenerationException => error.Message,
            LlmConfigNotConfiguredException => "Agent orchestrator chưa gắn cấu hình LLM đang hoạt động. Vào Cấu hình LLM để gắn provider/model.",
            null => "Lỗi không xác định khi lập kế hoạch.",
            _ => $"Lập kế hoạch thất bại: {error.Message}",
        };

        try
        {
            await sink.FailAndRejectOrphanedContentAsync(
                tenantId,
                sessionId,
                reason,
                expectedGeneration,
                scopedClock.UtcNow,
                ct).ConfigureAwait(false);
        }
        catch (OrchestrationPlanGenerationMismatchException)
        {
            // A newer plan has become durable; the old runner must not terminalize it.
        }
        catch (OrchestrationSessionNotRunningException)
        {
            // The user stopped the run; terminal state and content are intentionally preserved.
        }
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

    private static Guid? ParseOptionalGuid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id) || id == Guid.Empty)
            return null;
        return id;
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

    private static void CancelRunningSession(Guid sessionId)
    {
        if (RunningSessions.TryGetValue(sessionId, out var cts))
            cts.Cancel();
    }

    [LoggerMessage(EventId = 4102, Level = LogLevel.Error, Message = "Background orchestration run failed for session {SessionId}")]
    private static partial void LogBackgroundRunFailed(ILogger logger, Exception ex, Guid sessionId);
}
