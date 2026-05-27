namespace Clawbot.SharedKernel.Vectors;

public sealed record VectorRecord(string Id, ReadOnlyMemory<float> Embedding, IReadOnlyDictionary<string, string> Metadata);

public sealed record VectorMatch(string Id, float Score, IReadOnlyDictionary<string, string> Metadata);

public interface IVectorStore
{
    Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default);
    Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, ReadOnlyMemory<float> query, int topK, CancellationToken ct = default);
    Task DeleteAsync(string collection, IEnumerable<string> ids, CancellationToken ct = default);
}
