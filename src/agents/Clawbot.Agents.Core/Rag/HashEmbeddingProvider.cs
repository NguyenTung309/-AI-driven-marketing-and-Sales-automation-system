using System.Security.Cryptography;
using System.Text;

namespace Clawbot.Agents.Core.Rag;

/// <summary>
/// Deterministic 384-dim embedding derived from SHA-256 of the input. Stand-in so the
/// RAG pipeline is fully testable without a vendor embedding key. Swap for Voyage/OpenAI/SBERT
/// in M10 (track in RFC-001).
/// </summary>
public sealed class HashEmbeddingProvider : IEmbeddingProvider
{
    public int Dimension => 384;

    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default)
    {
        var vector = new float[Dimension];
        if (!string.IsNullOrEmpty(text))
        {
            var seed = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            for (var i = 0; i < Dimension; i++)
            {
                var b = seed[i % seed.Length];
                vector[i] = (b / 127.5f) - 1f;
            }
            Normalize(vector);
        }
        return Task.FromResult<ReadOnlyMemory<float>>(vector);
    }

    private static void Normalize(float[] v)
    {
        double sumSq = 0;
        for (var i = 0; i < v.Length; i++) sumSq += v[i] * v[i];
        var mag = Math.Sqrt(sumSq);
        if (mag <= double.Epsilon) return;
        for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / mag);
    }
}
