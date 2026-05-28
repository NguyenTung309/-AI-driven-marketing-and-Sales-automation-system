namespace Clawbot.Agents.Core.Rag;

public interface IEmbeddingProvider
{
    int Dimension { get; }
    Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default);
}
