namespace Clawbot.Agents.Core.Lead;

public sealed record DedupCandidate(Guid LeadId, Guid ContactId, string Reason, float Confidence);

public sealed record DedupRequest(
    Guid TenantId,
    Guid? ContactId,
    string? Phone,
    string? Email);

public interface ILeadDedupService
{
    Task<IReadOnlyList<DedupCandidate>> FindCandidatesAsync(DedupRequest request, CancellationToken ct = default);
}
