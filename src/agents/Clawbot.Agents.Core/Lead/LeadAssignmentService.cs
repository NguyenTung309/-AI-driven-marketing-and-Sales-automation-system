namespace Clawbot.Agents.Core.Lead;

public interface ILeadAssignmentService
{
    Task<Guid?> PickOwnerAsync(Guid tenantId, CancellationToken ct = default);
}

// A sale eligible for assignment plus their current open workload (lower = less busy).
public sealed record AssignmentCandidate(Guid UserId, int OpenLoad);

public sealed record AssignmentPool(IReadOnlyList<AssignmentCandidate> Candidates);

// M15 / Lead-2: assign hot leads to the least-busy sale ("sale rảnh nhất").
// Replaces the previous round-robin strategy. Tie-break by UserId for deterministic picks.
public sealed class LeastBusyLeadAssignmentService(IAssignmentPoolSource source) : ILeadAssignmentService
{
    private readonly IAssignmentPoolSource _source = source;

    public async Task<Guid?> PickOwnerAsync(Guid tenantId, CancellationToken ct = default)
    {
        var pool = await _source.LoadAsync(tenantId, ct).ConfigureAwait(false);
        if (pool.Candidates.Count == 0) return null;
        return pool.Candidates
            .OrderBy(c => c.OpenLoad)
            .ThenBy(c => c.UserId)
            .First().UserId;
    }
}

public interface IAssignmentPoolSource
{
    Task<AssignmentPool> LoadAsync(Guid tenantId, CancellationToken ct = default);
}
