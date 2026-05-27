using Clawbot.SharedKernel.Vectors;
using Qdrant.Client;

namespace Clawbot.Infrastructure.Vectors;

public sealed class QdrantVectorStore(QdrantClient client) : IVectorStore
{
    private readonly QdrantClient _client = client;

    public Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default) =>
        throw new NotImplementedException("TODO(student): map VectorRecord -> Qdrant points.");

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, ReadOnlyMemory<float> query, int topK, CancellationToken ct = default) =>
        throw new NotImplementedException("TODO(student): map Qdrant search result -> VectorMatch.");

    public Task DeleteAsync(string collection, IEnumerable<string> ids, CancellationToken ct = default) =>
        throw new NotImplementedException("TODO(student): delete points by ids.");
}
