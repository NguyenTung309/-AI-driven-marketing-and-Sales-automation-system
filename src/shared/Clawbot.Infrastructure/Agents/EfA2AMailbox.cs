using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

// EF-backed A2A mailbox. ponytail: claim is read-then-update, not atomic across processes;
// safe for the single-worker V2 scheduler. Upgrade to SELECT ... WITH (UPDLOCK) / queue if
// multiple workers fire the same session.
public sealed class EfA2AMailbox(AppDbContext db) : IA2AMailbox
{
    private readonly AppDbContext _db = db;

    public async Task<AgentA2AMessage> SendAsync(
        Guid tenantId,
        Guid sessionId,
        Guid? fromAgentDefinitionId,
        Guid toAgentDefinitionId,
        string taskId,
        string intent,
        string payloadJson,
        CancellationToken ct = default)
    {
        var message = AgentA2AMessage.Send(tenantId, sessionId, fromAgentDefinitionId, toAgentDefinitionId, taskId, intent, payloadJson, DateTimeOffset.UtcNow);
        _db.AgentA2AMessages.Add(message);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return message;
    }

    public async Task<AgentA2AMessage?> ClaimNextAsync(Guid tenantId, Guid sessionId, Guid toAgentDefinitionId, CancellationToken ct = default)
    {
        var message = await _db.AgentA2AMessages
            .Where(m => m.TenantId == tenantId && m.SessionId == sessionId && m.ToAgentDefinitionId == toAgentDefinitionId && m.Status == "pending")
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (message is null) return null;

        message.Claim(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return message;
    }

    public async Task CompleteAsync(Guid tenantId, Guid messageId, string payloadJson, DateTimeOffset at, CancellationToken ct = default)
    {
        var message = await LoadAsync(tenantId, messageId, ct).ConfigureAwait(false);
        if (message is null) return;
        message.Complete(payloadJson, at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task FailAsync(Guid tenantId, Guid messageId, string reason, DateTimeOffset at, CancellationToken ct = default)
    {
        var message = await LoadAsync(tenantId, messageId, ct).ConfigureAwait(false);
        if (message is null) return;
        message.Fail(reason, at);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentA2AMessage>> ListAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default) =>
        await _db.AgentA2AMessages
            .Where(m => m.TenantId == tenantId && m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    private async Task<AgentA2AMessage?> LoadAsync(Guid tenantId, Guid messageId, CancellationToken ct) =>
        await _db.AgentA2AMessages
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == messageId, ct)
            .ConfigureAwait(false);
}
