namespace Clawbot.Agents.Core.Lead;

public interface ILeadAssignmentService
{
    Task<Guid?> PickOwnerAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed record AssignmentPool(IReadOnlyList<Guid> EligibleUserIds);

public sealed class RoundRobinLeadAssignmentService(IAssignmentPoolSource source) : ILeadAssignmentService
{
    private static int _cursor;
    private readonly IAssignmentPoolSource _source = source;

    public async Task<Guid?> PickOwnerAsync(Guid tenantId, CancellationToken ct = default)
    {
        var pool = await _source.LoadAsync(tenantId, ct).ConfigureAwait(false);
        if (pool.EligibleUserIds.Count == 0) return null;
        var idx = Interlocked.Increment(ref _cursor);
        return pool.EligibleUserIds[Math.Abs(idx) % pool.EligibleUserIds.Count];
    }
}

public interface IAssignmentPoolSource
{
    Task<AssignmentPool> LoadAsync(Guid tenantId, CancellationToken ct = default);
}
