using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

// Persists autonomous run trace + plan + terminal status onto the existing AgentSession/agent_traces.
public sealed class AutonomousRunSink(AppDbContext db, IPiiRedactor pii, IClock clock) : IAutonomousRunSink
{
    private readonly AppDbContext _db = db;
    private readonly IPiiRedactor _pii = pii;
    private readonly IClock _clock = clock;

    public async Task TraceAsync(Guid tenantId, Guid sessionId, string taskId, string agent, string phase, string message, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        var redacted = await RedactAsync(message, ct).ConfigureAwait(false);
        session.AppendTrace(taskId, agent, phase, redacted, at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task PersistPlanAsync(Guid tenantId, Guid sessionId, OrchestrationPlanDocument plan, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        var redacted = await OrchestrationPlanRedactor.RedactAsync(plan, _pii, ct).ConfigureAwait(false);
        session.RecordRun(OrchestrationPlanJson.Serialize(redacted));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CompleteAsync(Guid tenantId, Guid sessionId, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        session.Finish(at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task FailAsync(Guid tenantId, Guid sessionId, string reason, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        session.AppendTrace(string.Empty, "orchestrator", "failed", await RedactAsync(reason, ct).ConfigureAwait(false), at);
        session.Fail(at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(Guid tenantId, Guid sessionId, DateTimeOffset at, CancellationToken ct = default)
    {
        var session = await LoadAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null) return;
        session.Cancel(at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<AgentSession?> LoadAsync(Guid tenantId, Guid sessionId, CancellationToken ct) =>
        await _db.AgentSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);

    private async Task<string> RedactAsync(string? text, CancellationToken ct) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : (await _pii.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;
}
