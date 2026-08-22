using System.Data;
using System.Text.Json;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.AgentService.Services;

// Persists autonomous run trace + plan + terminal status onto the existing AgentSession/agent_traces.
// SPEC-16 P3-4: also fires a targeted notification on terminal states (complete/fail/pending approval),
// so the initiating user is alerted without polling.
public sealed partial class AutonomousRunSink(
    AppDbContext db,
    IPiiRedactor pii,
    IClock clock,
    INotificationPublisher? publisher = null,
    ILogger<AutonomousRunSink>? logger = null,
    StackExchange.Redis.IConnectionMultiplexer? redis = null) : IAutonomousRunSink
{
    private readonly AppDbContext _db = db;
    private readonly IPiiRedactor _pii = pii;
    private readonly IClock _clock = clock;
    private readonly INotificationPublisher? _publisher = publisher;
    private readonly ILogger<AutonomousRunSink> _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AutonomousRunSink>.Instance;
    private readonly StackExchange.Redis.IConnectionMultiplexer? _redis = redis;
    private static readonly JsonSerializerOptions OrchestrationAuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    // C1: nudge the API-side relay so connected FE clients refetch this run instead of polling.
    // Fire-and-forget: losing an event only means the FE falls back to its slow poll.
    private void PublishRunEvent(Guid tenantId, Guid sessionId)
    {
        if (_redis is null) return;
        try
        {
            _redis.GetSubscriber().Publish(
                StackExchange.Redis.RedisChannel.Literal(Clawbot.Infrastructure.Notifications.RunEventsChannel.Name),
                $"{{\"tenantId\":\"{tenantId:D}\",\"sessionId\":\"{sessionId:D}\"}}",
                StackExchange.Redis.CommandFlags.FireAndForget);
        }
        catch (StackExchange.Redis.RedisException)
        {
            // FE polling fallback still covers it.
        }
    }

    public async Task TraceAsync(Guid tenantId, Guid sessionId, string taskId, string agent, string phase, string message, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        var redacted = await RedactAsync(message, ct).ConfigureAwait(false);
        session.AppendTrace(taskId, agent, phase, redacted, at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        PublishRunEvent(tenantId, sessionId);
    }

    public async Task PersistPlanAsync(
        Guid tenantId,
        Guid sessionId,
        OrchestrationPlanDocument plan,
        int expectedGeneration,
        bool requiresApproval = false,
        CancellationToken ct = default)
    {
        var redacted = await OrchestrationPlanRedactor.RedactAsync(plan, _pii, ct).ConfigureAwait(false);
        var planJson = OrchestrationPlanJson.Serialize(redacted);
        AgentSession? approvalSession = null;

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        try
        {
            var session = await LoadRequiredForPlanMutationAsync(tenantId, sessionId, ct)
                .ConfigureAwait(false);
            if (session.ReplanCount != expectedGeneration)
                throw new OrchestrationPlanGenerationMismatchException();
            if (session.Status != AgentSessionStatuses.Running)
                return;

            if (requiresApproval)
            {
                session.ApplyGeneratedPlan(planJson, requiresApproval: true);
                approvalSession = session;
            }
            else
            {
                session.RecordRun(planJson);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw;
        }

        PublishRunEvent(tenantId, sessionId);
        if (approvalSession is not null)
        {
            // EARS[WHEN a plan requires approval THE SYSTEM SHALL notify the initiating user that the plan is awaiting approval]
            await NotifyAsync(approvalSession, "orchestration_approval", "Kế hoạch chờ duyệt",
                Severity: "info",
                Body: $"Kế hoạch orchestration cho \"{approvalSession.Goal}\" đang chờ bạn duyệt.",
                Link: $"/agents/runs/{approvalSession.Id}", ct).ConfigureAwait(false);
        }
    }

    public async Task<bool> IsStoppedAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        return session?.Status != AgentSessionStatuses.Running;
    }

    public async Task<bool> TryAcknowledgePauseAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session?.Status != AgentSessionStatuses.PauseRequested) return false;

        session.AcknowledgePause();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        PublishRunEvent(tenantId, sessionId);
        return true;
    }

    // EARS[WHEN an orchestration task completes AND the tenant failure policy is "pause"
    // THE SYSTEM SHALL park the run in paused and notify the initiator to approve or amend that result]
    public async Task PauseForInterventionAsync(
        Guid tenantId,
        Guid sessionId,
        string taskId,
        string reason,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        var awaitingApproval = string.Equals(reason, "task_completed_awaiting_approval", StringComparison.Ordinal);
        var redactedReason = await RedactAsync(reason, ct).ConfigureAwait(false);
        var tracePhase = awaitingApproval ? "awaiting_approval" : "awaiting_intervention";
        var traceMessage = awaitingApproval
            ? "Bước đã hoàn tất và đang chờ bạn duyệt hoặc sửa nội dung trước khi chạy bước tiếp theo."
            : $"Đã tạm dừng để bạn xử lý: {redactedReason}.";
        AgentSession pausedSession;

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        try
        {
            var session = await LoadRequiredForPlanMutationAsync(tenantId, sessionId, ct)
                .ConfigureAwait(false);
            if (session.ReplanCount != expectedGeneration)
                throw new OrchestrationPlanGenerationMismatchException();
            if (session.Status is not (AgentSessionStatuses.Running or AgentSessionStatuses.PauseRequested))
                throw new OrchestrationSessionNotRunningException();

            session.AppendTrace(taskId ?? string.Empty, "orchestrator", tracePhase, traceMessage, at);
            session.PauseForIntervention();

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            pausedSession = session;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw;
        }

        PublishRunEvent(tenantId, sessionId);
        var title = awaitingApproval ? "Orchestration chờ bạn duyệt" : "Orchestration chờ bạn xử lý";
        var body = awaitingApproval
            ? $"Mục tiêu \"{pausedSession.Goal}\" đã hoàn tất một bước và đang chờ bạn duyệt hoặc sửa nội dung trước khi chạy tiếp."
            : $"Mục tiêu \"{pausedSession.Goal}\" đã tạm dừng để xử lý: {redactedReason}.";
        await NotifyAsync(pausedSession, "orchestration_intervention", title,
            Severity: awaitingApproval ? "info" : "warning",
            Body: body,
            Link: $"/agents/runs/{pausedSession.Id}", ct).ConfigureAwait(false);
    }

    public async Task<int> GetPlanGenerationAsync(
        Guid tenantId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        var session = await LoadRequiredAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        return session.ReplanCount;
    }

    public async Task<int> PersistReplanAndRejectSupersededContentAsync(
        Guid tenantId,
        Guid sessionId,
        int expectedGeneration,
        OrchestrationPlanDocument replacementPlan,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedGeneration);

        var redacted = await OrchestrationPlanRedactor.RedactAsync(replacementPlan, _pii, ct)
            .ConfigureAwait(false);
        var planJson = OrchestrationPlanJson.Serialize(redacted);

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        try
        {
            var session = await LoadRequiredForPlanMutationAsync(tenantId, sessionId, ct)
                .ConfigureAwait(false);
            if (session.Status != AgentSessionStatuses.Running)
                throw new OrchestrationSessionNotRunningException();
            var nextGeneration = session.ApplyReplan(planJson, expectedGeneration);
            await RejectOrphanedContentCoreAsync(
                tenantId,
                sessionId,
                expectedGeneration,
                at,
                ct).ConfigureAwait(false);

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            PublishRunEvent(tenantId, sessionId);
            return nextGeneration;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<int> RejectOrphanedContentCoreAsync(
        Guid tenantId,
        Guid sessionId,
        int? planGeneration,
        DateTimeOffset at,
        CancellationToken ct)
    {
        // An external publication may already be transmitted. The session lock held by every caller
        // serializes this check with the publisher's generation-validated claim, so the plan cannot
        // become stale after that irreversible side effect begins.
        await ThrowIfPublicationInProgressAsync(
            tenantId,
            sessionId,
            planGeneration,
            ct).ConfigureAwait(false);

        // Background orchestration scopes have no ambient HTTP tenant. Query filters must be bypassed,
        // then tenant/session predicates must be explicit to prevent cross-tenant cleanup.
        var items = await _db.ContentItems.IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId
                && item.OrchestrationSessionId == sessionId
                && item.OrchestrationPlanGeneration != null
                && (!planGeneration.HasValue
                    || item.OrchestrationPlanGeneration == planGeneration.Value)
                && item.OrchestrationOwnershipClaimedAt == null
                && (item.Status == "draft"
                    || item.Status == "approved"
                    || item.Status == "scheduled")
                && item.DeletedAt == null
                && item.ActivePublishAttemptId == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (items.Count == 0)
            return 0;

        var currentRevisions = items.ToDictionary(item => item.Id, item => item.ContentRevision);
        var itemIds = currentRevisions.Keys.ToArray();
        var activeTasks = await _db.ContentReviewTasks.IgnoreQueryFilters()
            .Where(task => task.TenantId == tenantId
                && itemIds.Contains(task.ContentItemId)
                && (task.Status == ContentReviewTask.StatusPending
                    || task.Status == ContentReviewTask.StatusLeased))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var pendingSchedules = await _db.ContentSchedules.IgnoreQueryFilters()
            .Where(schedule => schedule.TenantId == tenantId
                && itemIds.Contains(schedule.ContentItemId)
                && schedule.Status == ContentSchedule.StatusPending)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var schedule in pendingSchedules)
            schedule.Cancel(at, "orchestration_plan_failed");

        foreach (var item in items)
        {
            item.RejectForOrchestrationFailure(
                sessionId,
                item.OrchestrationPlanGeneration!.Value,
                at);
            _db.AuditLogs.Add(AuditLog.CreateBusinessEvent(
                tenantId,
                userId: null,
                "content.orchestration_plan_rejected",
                "content_item",
                item.Id,
                at,
                $"content-orchestration:{sessionId:N}:generation:{item.OrchestrationPlanGeneration}:rejected:{item.Id:N}",
                item.OrchestrationPlanGeneration.Value + 1,
                JsonSerializer.Serialize(
                    new
                    {
                        sessionId,
                        planGeneration = item.OrchestrationPlanGeneration,
                        reason = "orchestration_plan_failed",
                    },
                    OrchestrationAuditJsonOptions)));
        }

        foreach (var task in activeTasks)
        {
            if (currentRevisions.TryGetValue(task.ContentItemId, out var revision)
                && task.ContentRevision == revision)
            {
                task.CancelForOrchestrationFailure(at);
            }
        }

        return items.Count;
    }

    // Ownership claims and soft deletes are mutable local state. They cannot make an outbound
    // provider call safe to supersede, fail, or cancel, so this fence depends only on immutable
    // orchestration provenance and the active transmission marker.
    private async Task<bool> HasPublicationInProgressAsync(
        Guid tenantId,
        Guid sessionId,
        int? planGeneration,
        CancellationToken ct) =>
        await _db.ContentItems.IgnoreQueryFilters()
            .AnyAsync(item => item.TenantId == tenantId
                && item.OrchestrationSessionId == sessionId
                && item.OrchestrationPlanGeneration != null
                && (!planGeneration.HasValue
                    || item.OrchestrationPlanGeneration == planGeneration.Value)
                && item.ActivePublishAttemptId != null,
                ct)
            .ConfigureAwait(false);

    private async Task ThrowIfPublicationInProgressAsync(
        Guid tenantId,
        Guid sessionId,
        int? planGeneration,
        CancellationToken ct)
    {
        if (await HasPublicationInProgressAsync(tenantId, sessionId, planGeneration, ct)
            .ConfigureAwait(false))
        {
            throw new OrchestrationPublicationInProgressException();
        }
    }

    public async Task CompleteAsync(
        Guid tenantId,
        Guid sessionId,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        AgentSession? completedSession = null;
        var pauseAcknowledged = false;
        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        try
        {
            var session = await LoadRequiredForPlanMutationAsync(tenantId, sessionId, ct)
                .ConfigureAwait(false);
            if (session.ReplanCount != expectedGeneration)
                throw new OrchestrationPlanGenerationMismatchException();
            if (session.Status == AgentSessionStatuses.PauseRequested)
            {
                session.AcknowledgePause();
                pauseAcknowledged = true;
            }
            else
            {
                if (session.Status != AgentSessionStatuses.Running)
                    throw new OrchestrationSessionNotRunningException();

                session.Finish(at);
                completedSession = session;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw;
        }

        PublishRunEvent(tenantId, sessionId);
        if (pauseAcknowledged)
            throw new OrchestrationSessionNotRunningException();

        // EARS[WHEN a run completes THE SYSTEM SHALL notify the initiating user with the outcome]
        await NotifyAsync(completedSession!, "orchestration_completed", "Hoàn thành orchestration",
            Severity: "success",
            Body: $"Mục tiêu \"{completedSession!.Goal}\" đã hoàn thành.",
            Link: $"/agents/runs/{completedSession!.Id}", ct).ConfigureAwait(false);
    }

    public async Task<int> FailAndRejectOrphanedContentAsync(
        Guid tenantId,
        Guid sessionId,
        string reason,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        var redactedReason = await RedactAsync(reason, ct).ConfigureAwait(false);
        AgentSession? failedSession = null;
        var rejectedCount = 0;
        var terminalIntentDeferred = false;

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        try
        {
            var session = await LoadRequiredForPlanMutationAsync(tenantId, sessionId, ct)
                .ConfigureAwait(false);
            if (session.ReplanCount != expectedGeneration)
                throw new OrchestrationPlanGenerationMismatchException();
            if (session.Status is not (AgentSessionStatuses.Running or AgentSessionStatuses.PauseRequested))
                throw new OrchestrationSessionNotRunningException();

            if (await HasPublicationInProgressAsync(tenantId, sessionId, expectedGeneration, ct)
                .ConfigureAwait(false))
            {
                session.DeferFailure(redactedReason, expectedGeneration, at);
                session.AppendTrace(
                    string.Empty,
                    "orchestrator",
                    "failure_pending_publication_settlement",
                    redactedReason,
                    at);
                terminalIntentDeferred = true;
            }
            else
            {
                rejectedCount = await RejectOrphanedContentCoreAsync(
                    tenantId,
                    sessionId,
                    expectedGeneration,
                    at,
                    ct).ConfigureAwait(false);
                session.AppendTrace(string.Empty, "orchestrator", "failed", redactedReason, at);
                session.Fail(at);
                failedSession = session;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw;
        }

        PublishRunEvent(tenantId, sessionId);
        if (terminalIntentDeferred)
            return 0;

        await NotifyAsync(failedSession!, "orchestration_failed", "Orchestration thất bại",
            Severity: "warning",
            Body: BuildFailBody(failedSession!.Goal, redactedReason),
            Link: $"/agents/runs/{failedSession!.Id}", ct).ConfigureAwait(false);
        return rejectedCount;
    }

    public async Task FailAsync(
        Guid tenantId,
        Guid sessionId,
        string reason,
        int expectedGeneration,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        await FailAndRejectOrphanedContentAsync(
            tenantId,
            sessionId,
            reason,
            expectedGeneration,
            at,
            ct).ConfigureAwait(false);
    }

    // SPEC-16 Module M-6: when the failure looks like an auth/token problem, surface a re-auth hint so the admin
    // reconnects the channel instead of the run failing silently with an opaque reason.
    private static string BuildFailBody(string? goal, string reason)
    {
        var lower = (reason ?? string.Empty).ToLowerInvariant();
        if (lower.Contains("401") || lower.Contains("unauthorized") || lower.Contains("expired") || lower.Contains("token"))
            return $"Mục tiêu \"{goal}\" thất bại: {reason}. Có thể cần kết nối lại kênh (re-auth) tại Cấu hình kênh.";
        return $"Mục tiêu \"{goal}\" thất bại: {reason}";
    }

    public async Task CancelAsync(
        Guid tenantId,
        Guid sessionId,
        int expectedGeneration,
        byte[]? expectedRowVersion,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        try
        {
            var session = await LoadRequiredForPlanMutationAsync(tenantId, sessionId, ct)
                .ConfigureAwait(false);
            if (session.ReplanCount != expectedGeneration)
                throw new OrchestrationPlanGenerationMismatchException();
            if (expectedRowVersion is { Length: > 0 }
                && (session.RowVersion is not { Length: > 0 }
                    || !session.RowVersion.SequenceEqual(expectedRowVersion)))
            {
                throw new OrchestrationSessionEtagMismatchException();
            }

            if (await HasPublicationInProgressAsync(tenantId, sessionId, expectedGeneration, ct)
                .ConfigureAwait(false))
            {
                session.DeferCancellation(expectedGeneration, at);
            }
            else
            {
                await RejectOrphanedContentCoreAsync(
                    tenantId,
                    sessionId,
                    expectedGeneration,
                    at,
                    ct).ConfigureAwait(false);
                session.Cancel(at);
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        PublishRunEvent(tenantId, sessionId);
    }

    public async Task<int> FinalizeDeferredTerminalsAsync(CancellationToken ct = default)
    {
        var candidates = await _db.AgentSessions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(session => (session.Status == AgentSessionStatuses.Cancelling
                    || session.Status == AgentSessionStatuses.Failing)
                && session.PendingTerminalRequestedAt != null)
            .OrderBy(session => session.PendingTerminalRequestedAt)
            .ThenBy(session => session.Id)
            .Select(session => new PendingTerminalCandidate(session.TenantId, session.Id))
            .Take(50)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var finalized = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await TryFinalizeDeferredTerminalAsync(candidate, ct).ConfigureAwait(false))
                    finalized++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _db.ChangeTracker.Clear();
                LogDeferredTerminalFinalizationFailed(
                    _logger,
                    exception,
                    candidate.TenantId,
                    candidate.SessionId);
            }
        }

        return finalized;
    }

    private async Task<bool> TryFinalizeDeferredTerminalAsync(
        PendingTerminalCandidate candidate,
        CancellationToken ct)
    {
        AgentSession? terminalSession = null;
        string? failureReason = null;
        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        try
        {
            var session = await LoadRequiredForPlanMutationAsync(
                    candidate.TenantId,
                    candidate.SessionId,
                    ct)
                .ConfigureAwait(false);
            if (session.Status is not (AgentSessionStatuses.Cancelling or AgentSessionStatuses.Failing)
                || session.PendingTerminalGeneration is not { } pendingGeneration
                || session.PendingTerminalRequestedAt is null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return false;
            }

            if (session.ReplanCount != pendingGeneration
                || await HasPublicationInProgressAsync(
                    candidate.TenantId,
                    candidate.SessionId,
                    pendingGeneration,
                    ct).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return false;
            }

            await RejectOrphanedContentCoreAsync(
                candidate.TenantId,
                candidate.SessionId,
                pendingGeneration,
                _clock.UtcNow,
                ct).ConfigureAwait(false);
            failureReason = session.Status == AgentSessionStatuses.Failing
                ? session.PendingTerminalReason
                : null;
            session.FinalizeDeferredTerminal(_clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            terminalSession = session;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        PublishRunEvent(candidate.TenantId, candidate.SessionId);
        if (failureReason is not null)
        {
            await NotifyAsync(
                terminalSession!,
                "orchestration_failed",
                "Orchestration thất bại",
                Severity: "warning",
                Body: BuildFailBody(terminalSession!.Goal, failureReason),
                Link: $"/agents/runs/{terminalSession!.Id}",
                ct).ConfigureAwait(false);
        }

        return true;
    }

    private sealed record PendingTerminalCandidate(Guid TenantId, Guid SessionId);

    private async Task<AgentSession?> LoadAsync(Guid tenantId, Guid sessionId, CancellationToken ct) =>
        await _db.AgentSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);

    private async Task<AgentSession> LoadRequiredAsync(
        Guid tenantId,
        Guid sessionId,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenant_id_required", nameof(tenantId));
        if (sessionId == Guid.Empty)
            throw new ArgumentException("session_id_required", nameof(sessionId));

        return await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("orchestration_session_not_found");
    }

    private async Task<AgentSession> LoadRequiredForPlanMutationAsync(
        Guid tenantId,
        Guid sessionId,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenant_id_required", nameof(tenantId));
        if (sessionId == Guid.Empty)
            throw new ArgumentException("session_id_required", nameof(sessionId));

        return await OrchestrationSessionGenerationFence.LockAsync(_db, tenantId, sessionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("orchestration_session_not_found");
    }

    private async Task<string> RedactAsync(string? text, CancellationToken ct) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : (await _pii.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;

    // Best-effort notification: a publish failure is logged and never breaks the run (the run already persisted its terminal state).
    private async Task NotifyAsync(AgentSession session, string type, string title, string Severity, string Body, string Link, CancellationToken ct)
    {
        if (_publisher is null) return;
        try
        {
            await _publisher.PublishAsync(new NotificationRequest(
                session.TenantId, session.UserId, type, title, Severity, Body, Link), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(_logger, ex, session.TenantId, session.Id, type);
        }
    }

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "AutonomousRunSink notification failed tenant={TenantId} session={SessionId} type={Type}")]
    private static partial void LogNotifyFailed(ILogger logger, Exception ex, Guid tenantId, Guid sessionId, string type);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Error, Message = "Deferred orchestration terminal finalization failed tenant={TenantId} session={SessionId}")]
    private static partial void LogDeferredTerminalFinalizationFailed(
        ILogger logger,
        Exception exception,
        Guid tenantId,
        Guid sessionId);
}
