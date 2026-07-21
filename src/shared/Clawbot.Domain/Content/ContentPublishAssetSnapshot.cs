namespace Clawbot.Domain.Content;

public sealed record ContentPublishAssetSnapshot(
    Guid AssetId,
    string Sha256Hex,
    int SortOrder,
    string ContentType,
    long SizeBytes);
