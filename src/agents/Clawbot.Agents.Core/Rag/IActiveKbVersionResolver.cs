namespace Clawbot.Agents.Core.Rag;

public interface IActiveKbVersionResolver
{
    // moduleCodes rỗng/null = không lọc theo module (lấy mọi bản deployed của tenant).
    Task<IReadOnlySet<string>> ResolveActiveVersionIdsAsync(Guid tenantId, IReadOnlyCollection<string>? moduleCodes, CancellationToken ct = default);
}
