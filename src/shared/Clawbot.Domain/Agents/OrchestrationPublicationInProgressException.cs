namespace Clawbot.Domain.Agents;

// A plan generation with an active external publication cannot be superseded safely.
public sealed class OrchestrationPublicationInProgressException : InvalidOperationException
{
    public OrchestrationPublicationInProgressException()
        : base("orchestration_publication_in_progress")
    {
    }
}
