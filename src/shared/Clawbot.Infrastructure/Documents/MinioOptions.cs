namespace Clawbot.Infrastructure.Documents;

// Config module for MinIO document storage (external service). Bind from "Docs:Storage:Minio".
public sealed class MinioOptions
{
    public const string SectionName = "Docs:Storage:Minio";

    public string? Endpoint { get; init; }
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public bool Secure { get; init; }
    public string Bucket { get; init; } = "clawbot-docs";
    public string PublicBaseUrl { get; init; } = string.Empty;
}
