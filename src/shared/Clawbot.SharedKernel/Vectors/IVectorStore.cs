namespace Clawbot.SharedKernel.Vectors;

public sealed record VectorRecord(string Id, ReadOnlyMemory<float> Embedding, IReadOnlyDictionary<string, string> Metadata);

public sealed record VectorMatch(string Id, float Score, IReadOnlyDictionary<string, string> Metadata);

public sealed record VectorMetadataFilter(string Field, IReadOnlyList<string> Values);

public interface IVectorStore
{
    Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default);
    Task<IReadOnlyList<VectorMatch>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> query,
        int topK,
        IReadOnlyList<VectorMetadataFilter>? filters = null,
        CancellationToken ct = default);
    Task DeleteAsync(string collection, IEnumerable<string> ids, CancellationToken ct = default);
}
