using Clawbot.Domain.Common;

namespace Clawbot.Domain.KnowledgeBase;

public sealed class KbVersion : Entity<Guid>
{
    public Guid KbModuleId { get; private set; }
    public int Version { get; private set; }
    public string ContentMd { get; private set; } = string.Empty;
    // Embedding stored as JSON-serialized float array in SQL Server (NVARCHAR(MAX)).
    // For vector search use the parallel Qdrant collection — keyed by KbVersion.Id.
    public string? Embedding { get; private set; }
    public decimal? AccuracyScore { get; private set; }
    public string Status { get; private set; } = "draft";   // draft|deployed|archived
    public DateTimeOffset? DeployedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private KbVersion() { }

    public static KbVersion Create(Guid kbModuleId, int version, string contentMd, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            KbModuleId = kbModuleId,
            Version = version,
            ContentMd = contentMd,
            CreatedAt = createdAt,
        };

    public void Deploy(DateTimeOffset at)
    {
        Status = "deployed";
        DeployedAt = at;
    }

    public void RecordAccuracy(decimal score) => AccuracyScore = score;

    public void SetEmbeddingJson(string embeddingJson) => Embedding = embeddingJson;
}
