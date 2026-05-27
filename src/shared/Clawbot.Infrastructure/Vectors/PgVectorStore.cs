using Clawbot.SharedKernel.Vectors;

namespace Clawbot.Infrastructure.Vectors;

public sealed class PgVectorStore : IVectorStore
{
    public Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default) =>
        throw new NotImplementedException("TODO(student): implement upsert against pgvector table.");

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, ReadOnlyMemory<float> query, int topK, CancellationToken ct = default) =>
        throw new NotImplementedException("TODO(student): implement cosine search against pgvector.");

    public Task DeleteAsync(string collection, IEnumerable<string> ids, CancellationToken ct = default) =>
        throw new NotImplementedException("TODO(student): implement delete by ids.");
}
