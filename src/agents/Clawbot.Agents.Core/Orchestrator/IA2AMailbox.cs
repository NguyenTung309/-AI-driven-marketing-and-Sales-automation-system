using Clawbot.Domain.Agents;

namespace Clawbot.Agents.Core.Orchestrator;

// Persistence boundary for agent-to-agent messages. Implementations live in AgentService
// (Core cannot reference Infrastructure). Claim is atomic per (tenant, session, target agent).
public interface IA2AMailbox
{
    Task<AgentA2AMessage> SendAsync(
        Guid tenantId,
        Guid sessionId,
        Guid? fromAgentDefinitionId,
        Guid toAgentDefinitionId,
        string taskId,
        string intent,
        string payloadJson,
        CancellationToken ct = default);

    Task<AgentA2AMessage?> ClaimNextAsync(Guid tenantId, Guid sessionId, Guid toAgentDefinitionId, CancellationToken ct = default);

    Task CompleteAsync(Guid tenantId, Guid messageId, string payloadJson, DateTimeOffset at, CancellationToken ct = default);

    Task FailAsync(Guid tenantId, Guid messageId, string reason, DateTimeOffset at, CancellationToken ct = default);

    Task<IReadOnlyList<AgentA2AMessage>> ListAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);
}
