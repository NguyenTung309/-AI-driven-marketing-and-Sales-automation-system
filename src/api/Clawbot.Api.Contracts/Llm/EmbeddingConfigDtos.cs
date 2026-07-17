namespace Clawbot.Api.Contracts.Llm;

public sealed record EmbeddingConfigDto(
    Guid Id,
    string Provider,
    string ModelId,
    string? DisplayName,
    bool HasApiKey,
    string? BaseUrl,
    int Dimension,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateEmbeddingConfigRequest(
    string Provider,
    string ModelId,
    int Dimension,
    string? ApiKey = null,
    string? DisplayName = null,
    string? BaseUrl = null);

public sealed record UpdateEmbeddingConfigRequest(
    string Provider,
    string ModelId,
    int Dimension,
    string? DisplayName = null,
    string? BaseUrl = null);

public sealed record RotateEmbeddingKeyRequest(string ApiKey);

// RetrievalMode: "vector" (có embedding config -> Qdrant) | "llm" (mặc định — LLM của tenant chọn đoạn KB).
public sealed record EmbeddingStatusResponse(
    bool Configured,
    string Provider,
    string ModelId,
    int Dimension,
    string Source,
    bool IsFallback,
    string? DisplayName = null,
    string RetrievalMode = "vector");
