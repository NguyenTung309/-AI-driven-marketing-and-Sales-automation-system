namespace Clawbot.Agents.Core.Orchestrator;

public sealed class SerializingAgent(IAgent inner, SemaphoreSlim gate) : IAgent
{
    private readonly IAgent _inner = inner;
    private readonly SemaphoreSlim _gate = gate;

    public string Name => _inner.Name;

    public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await _inner.ExecuteAsync(task, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
