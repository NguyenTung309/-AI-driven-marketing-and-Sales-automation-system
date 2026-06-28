namespace Clawbot.Agents.Core.Orchestrator;

public sealed record AutonomousRunRequest(
    Guid TenantId,
    Guid SessionId,
    string Goal,
    string Source,
    bool RequiresApproval,
    bool DryRun = false);

public sealed record AutonomousRunResult(string Status, string? Reason, int RoundCount)
{
    public static AutonomousRunResult Completed(int rounds) => new("completed", null, rounds);
    public static AutonomousRunResult PendingApproval(int rounds) => new("pending_approval", null, rounds);
    public static AutonomousRunResult Failed(string reason, int rounds) => new("failed", reason, rounds);
    public static AutonomousRunResult Cancelled(int rounds) => new("cancelled", null, rounds);
}

public sealed class AutonomousOrchestratorOptions
{
    public int MaxRounds { get; init; } = 3;
    public int MaxConcurrency { get; init; } = 3;        // ponytail: sequential execution for now; cap reserved for parallel upgrade
    public decimal PerTaskEstimateUsd { get; init; } = 0.01m;

    // Transient (timeout / 5xx / 429) failures retry the SAME task without burning a replan round.
    // Only logical failures (non-transient exceptions or AgentResult.Success=false) count toward MaxRounds.
    public int MaxTransientRetries { get; init; } = 2;
    public int TransientBackoffBaseMs { get; init; } = 2000;
}
