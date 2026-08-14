namespace Clawbot.Agents.Core.Orchestrator;

public interface IAutonomousOrchestrator
{
    Task<AutonomousRunResult> RunAsync(
        AutonomousRunRequest request,
        CancellationToken ct = default);

    Task<AutonomousRunResult> RunExistingPlanAsync(
        AutonomousRunRequest request,
        OrchestrationPlanDocument plan,
        CancellationToken ct = default);
}
