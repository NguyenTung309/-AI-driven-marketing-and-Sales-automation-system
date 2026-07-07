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
        var userId = ParseOptionalGuid(request.UserId);
        // SPEC-16 P3-3: attribute the session to the initiating user so terminal notifications target them.
        var session = AgentSession.Start(tenantId, orchestratorAgentId, conversationId: null, redactedGoal, now, userId: userId);
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
                catch (Exception ex)
                {
                    LogBackgroundRunFailed(_logger, ex, sessionId);
                    await MarkBackgroundRunFailedAsync(sessionId, ex, CancellationToken.None).ConfigureAwait(false);
                }
            }, CancellationToken.None);
            return ToResponse(session);
        }

        // No scope factory (tests / inline host): plan + execute synchronously within the request.
        var (costBlocked, costReason) = await PlanAndExecuteAsync(session, request.Goal, request.DryRun, ct).ConfigureAwait(false);
        return ToResponse(session, costBlocked, costReason);
    }

    private async Task PlanAndRunPersistedAsync(Guid sessionId, string goal, bool dryRun, CancellationToken ct)
    {
        var session = await _db.AgentSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null)
            return;

        await PlanAndExecuteAsync(session, goal, dryRun, ct).ConfigureAwait(false);
    }

    // Generate and execute through V2. The session placeholder already exists, so progress survives F5.
    private async Task<(bool CostBlocked, string? CostReason)> PlanAndExecuteAsync(AgentSession session, string goal, bool dryRun, CancellationToken ct)
    {
        var requireApproval = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == session.TenantId)
            .Select(t => t.RequireOrchestrationApproval)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var existingPlan = OrchestrationPlanJson.TryParse(session.PlanJson);
        var result = existingPlan?.Tasks is { Count: > 0 } && session.Status == AgentSessionStatuses.Running
            ? await _autonomous.RunExistingPlanAsync(new AutonomousRunRequest(session.TenantId, session.Id, goal, "manual", RequiresApproval: false, DryRun: dryRun), existingPlan, ct).ConfigureAwait(false)
            : await _autonomous.RunAsync(new AutonomousRunRequest(session.TenantId, session.Id, goal, "manual", requireApproval, DryRun: dryRun), ct).ConfigureAwait(false);

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
                await MarkBackgroundRunFailedAsync(sessionId, ex, CancellationToken.None).ConfigureAwait(false);
            }
        }, CancellationToken.None);
    }

    // Records a user-safe failure trace before failing the session, so an exception escaping the
    // background task (e.g. planner LLM error) shows a reason in the FE instead of a bare "failed".
    private async Task MarkBackgroundRunFailedAsync(Guid sessionId, Exception? error, CancellationToken ct)
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

        var reason = error switch
        {
            PlanGenerationException => error.Message,
            LlmConfigNotConfiguredException => "Agent orchestrator chưa gắn cấu hình LLM đang hoạt động. Vào Cấu hình LLM để gắn provider/model.",
            null => "Lỗi không xác định khi lập kế hoạch.",
            _ => $"Lập kế hoạch thất bại: {error.Message}",
        };
        session.AppendTrace(string.Empty, OrchestratorAgentCode, "planning_failed", reason, scopedClock.UtcNow);
        session.Fail(scopedClock.UtcNow);
        await scopedDb.SaveChangesAsync(ct).ConfigureAwait(false);
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
