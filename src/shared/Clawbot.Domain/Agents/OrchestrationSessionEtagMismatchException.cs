namespace Clawbot.Domain.Agents;

public sealed class OrchestrationSessionEtagMismatchException : InvalidOperationException
{
    public OrchestrationSessionEtagMismatchException()
        : base("orchestration_session_etag_mismatch")
    {
    }
}
