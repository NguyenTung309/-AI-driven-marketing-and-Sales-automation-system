namespace Clawbot.Infrastructure.Vectors;

// Config module for the Qdrant vector DB (external service). Bind from "Vector:Qdrant".
public sealed class QdrantOptions
{
    public const string SectionName = "Vector:Qdrant";

    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 6334;
    public bool UseTls { get; init; }
    public string? ApiKey { get; init; }
}
