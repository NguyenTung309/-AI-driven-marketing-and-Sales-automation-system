using Clawbot.SharedKernel.Vectors;
using Google.Protobuf.Collections;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Clawbot.Infrastructure.Vectors;

public sealed class QdrantVectorStore(QdrantClient client) : IVectorStore
{
    private readonly QdrantClient _client = client;

    public async Task UpsertAsync(string collection, IEnumerable<VectorRecord> records, CancellationToken ct = default)
    {
        var points = records.Select(ToPoint).ToList();
        if (points.Count == 0) return;

        await EnsureCollectionAsync(collection, (uint)points[0].Vectors.Vector.Data.Count, ct).ConfigureAwait(false);
        await _client.UpsertAsync(collection, points, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> query,
        int topK,
        CancellationToken ct = default)
    {
        await EnsureCollectionAsync(collection, (uint)query.Length, ct).ConfigureAwait(false);
        var hits = await _client.SearchAsync(
            collection,
            query.ToArray(),
            limit: (ulong)Math.Max(1, topK),
            payloadSelector: true,
            cancellationToken: ct).ConfigureAwait(false);

        return hits.Select(h => new VectorMatch(
            Id: h.Id.Uuid,
            Score: h.Score,
            Metadata: (IReadOnlyDictionary<string, string>)ToStringDict(h.Payload))).ToList();
    }

    public async Task DeleteAsync(string collection, IEnumerable<string> ids, CancellationToken ct = default)
    {
        var guids = new List<Guid>();
        foreach (var id in ids)
        {
            if (Guid.TryParse(id, out var g)) guids.Add(g);
        }
        if (guids.Count == 0) return;
        await _client.DeleteAsync(collection, guids, cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task EnsureCollectionAsync(string collection, uint vectorSize, CancellationToken ct)
    {
        var existing = await _client.ListCollectionsAsync(ct).ConfigureAwait(false);
        if (existing.Any(c => string.Equals(c, collection, StringComparison.Ordinal))) return;

        await _client.CreateCollectionAsync(
            collection,
            new VectorParams { Size = vectorSize, Distance = Distance.Cosine },
            cancellationToken: ct).ConfigureAwait(false);
    }

    private static PointStruct ToPoint(VectorRecord r)
    {
        var point = new PointStruct
        {
            Id = new PointId { Uuid = r.Id },
            Vectors = r.Embedding.ToArray(),
        };
        foreach (var kv in r.Metadata)
            point.Payload[kv.Key] = new Value { StringValue = kv.Value };
        return point;
    }

    private static Dictionary<string, string> ToStringDict(MapField<string, Value> payload)
    {
        var dict = new Dictionary<string, string>(payload.Count, StringComparer.Ordinal);
        foreach (var (k, v) in payload)
            dict[k] = v.KindCase == Value.KindOneofCase.StringValue ? v.StringValue : v.ToString();
        return dict;
    }
}
