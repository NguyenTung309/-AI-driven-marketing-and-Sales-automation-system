namespace Clawbot.Agents.Core.Rag;

public sealed record RagChunk(string KbVersionId, string KbModuleCode, string Snippet, float Score);

public sealed record RagRequest(Guid TenantId, string? KbModuleCode, string Query, int TopK = 5);

public interface IRagRetriever
{
    Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default);
}
