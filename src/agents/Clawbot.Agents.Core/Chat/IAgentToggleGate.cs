namespace Clawbot.Agents.Core.Chat;

/// <summary>
/// Gates per-agent-type auto-actions per tenant (M25). Lets Admin disable an agent's
/// automatic behaviour (e.g. chat auto-reply) without stopping the gRPC service.
/// Default impl is always-enabled so behaviour is unchanged until an AgentConfig is stopped.
/// </summary>
public interface IAgentToggleGate
{
    Task<bool> IsAutoActionEnabledAsync(Guid tenantId, string agentType, CancellationToken ct = default);
}

public sealed class AlwaysEnabledAgentToggleGate : IAgentToggleGate
{
    public Task<bool> IsAutoActionEnabledAsync(Guid tenantId, string agentType, CancellationToken ct = default) =>
        Task.FromResult(true);
}
