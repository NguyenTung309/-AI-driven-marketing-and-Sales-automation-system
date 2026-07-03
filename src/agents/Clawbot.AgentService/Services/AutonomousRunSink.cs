using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.AgentService.Services;

// Persists autonomous run trace + plan + terminal status onto the existing AgentSession/agent_traces.
// SPEC-16 P3-4: also fires a targeted notification on terminal states (complete/fail/pending approval),
// mirroring the AdsAgentGrpcService pattern so the initiating user is alerted without polling.
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

    public async Task PersistPlanAsync(Guid tenantId, Guid sessionId, OrchestrationPlanDocument plan, bool requiresApproval = false, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        var redacted = await OrchestrationPlanRedactor.RedactAsync(plan, _pii, ct).ConfigureAwait(false);
        var planJson = OrchestrationPlanJson.Serialize(redacted);
        if (requiresApproval && session.Status == AgentSessionStatuses.Running)
        {
            session.ApplyGeneratedPlan(planJson, requiresApproval: true);
            // EARS[WHEN a plan requires approval THE SYSTEM SHALL notify the initiating user that the plan is awaiting approval]
            await NotifyAsync(session, "orchestration_approval", "Kế hoạch chờ duyệt",
                Severity: "info",
                Body: $"Kế hoạch orchestration cho \"{session.Goal}\" đang chờ bạn duyệt.",
                Link: $"/agents/runs/{session.Id}", ct).ConfigureAwait(false);
        }
        else
            session.RecordRun(planJson);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        PublishRunEvent(tenantId, sessionId);
    }

    public async Task<bool> IsStoppedAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        return session?.Status is AgentSessionStatuses.Paused or AgentSessionStatuses.Cancelled;
    }

    public async Task CompleteAsync(Guid tenantId, Guid sessionId, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        session.Finish(at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        PublishRunEvent(tenantId, sessionId);
        // EARS[WHEN a run completes THE SYSTEM SHALL notify the initiating user with the outcome]
        await NotifyAsync(session, "orchestration_completed", "Hoàn thành orchestration",
            Severity: "success",
            Body: $"Mục tiêu \"{session.Goal}\" đã hoàn thành.",
            Link: $"/agents/runs/{session.Id}", ct).ConfigureAwait(false);
    }

    public async Task FailAsync(Guid tenantId, Guid sessionId, string reason, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        session.AppendTrace(string.Empty, "orchestrator", "failed", await RedactAsync(reason, ct).ConfigureAwait(false), at);
        session.Fail(at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        PublishRunEvent(tenantId, sessionId);
        await NotifyAsync(session, "orchestration_failed", "Orchestration thất bại",
            Severity: "warning",
            Body: BuildFailBody(session.Goal, reason),
            Link: $"/agents/runs/{session.Id}", ct).ConfigureAwait(false);
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

    public async Task CancelAsync(Guid tenantId, Guid sessionId, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        session.Cancel(at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        PublishRunEvent(tenantId, sessionId);
    }

    private async Task<AgentSession?> LoadAsync(Guid tenantId, Guid sessionId, CancellationToken ct) =>
        await _db.AgentSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);

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
}
