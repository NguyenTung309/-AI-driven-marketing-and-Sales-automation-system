namespace Clawbot.Domain.Agents;

public sealed class OrchestrationSessionNotRunningException : InvalidOperationException
{
    public OrchestrationSessionNotRunningException()
        : base("orchestration_session_not_running")
    {
    }
}
