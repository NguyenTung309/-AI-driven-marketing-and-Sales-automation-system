namespace Clawbot.Domain.Agents;

public sealed class OrchestrationPlanGenerationMismatchException : InvalidOperationException
{
    public OrchestrationPlanGenerationMismatchException()
        : base("orchestration_plan_generation_mismatch")
    {
    }
}
