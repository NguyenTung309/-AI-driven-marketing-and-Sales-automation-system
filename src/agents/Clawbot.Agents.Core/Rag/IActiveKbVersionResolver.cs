namespace Clawbot.Agents.Core.Rag;

public interface IActiveKbVersionResolver
{
    Task<IReadOnlySet<string>> ResolveActiveVersionIdsAsync(Guid tenantId, string? moduleCode, CancellationToken ct = default);
}
