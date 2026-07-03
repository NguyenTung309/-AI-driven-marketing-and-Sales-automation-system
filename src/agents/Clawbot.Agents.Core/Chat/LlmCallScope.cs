namespace Clawbot.Agents.Core.Chat;

// Ambient (tenant, agent) context for an LLM call. Set at each agent entry point so deeply-nested
// singleton skills (summarizer, translator, …) can resolve their provider config without threading
// tenant/agent through every method signature. Mirrors the ITenantAccessor pattern.
public readonly record struct LlmCallContext(Guid TenantId, string AgentCode, DateTimeOffset? CostAt = null, Guid? ReservationId = null, Guid? SessionId = null);

public interface ILlmCallScope
{
    LlmCallContext? Current { get; }

    // Establish the ambient context for the enclosing async flow; dispose to restore the prior value.
    IDisposable Begin(Guid tenantId, string agentCode, DateTimeOffset? costAt = null, Guid? reservationId = null, Guid? sessionId = null);
}

// Singleton-safe via AsyncLocal: the value flows down the await chain and is readable from the
// singleton delegating chat client + singleton skills.
public sealed class LlmCallScope : ILlmCallScope
{
    private static readonly AsyncLocal<LlmCallContext?> Ambient = new();

    public LlmCallContext? Current => Ambient.Value;

    public IDisposable Begin(Guid tenantId, string agentCode, DateTimeOffset? costAt = null, Guid? reservationId = null, Guid? sessionId = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(agentCode)) throw new ArgumentException("agentCode required", nameof(agentCode));

        var previous = Ambient.Value;
        Ambient.Value = new LlmCallContext(tenantId, agentCode, costAt ?? previous?.CostAt, reservationId ?? previous?.ReservationId, sessionId ?? previous?.SessionId);
        return new Restore(previous);
    }

    private sealed class Restore(LlmCallContext? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
