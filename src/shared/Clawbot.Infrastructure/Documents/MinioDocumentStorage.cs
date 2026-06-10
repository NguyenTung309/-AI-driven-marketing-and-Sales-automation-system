using Clawbot.Agents.Core.Docs;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;

namespace Clawbot.Infrastructure.Documents;

// MinIO-backed document storage (M17): uploads the rendered PDF and returns a 7-day presigned GET URL.
// Config-gated via Docs:Storage:Minio:* — registered only when an endpoint is configured; otherwise
// LocalDocumentStorage (registered by DocsModule) remains the fallback.
public sealed class MinioDocumentStorage : IDocumentStorage
{
    private const int ExpirySeconds = 7 * 24 * 60 * 60; // 7 days

    private readonly IMinioClient _client;
    private readonly string _bucket;

    public MinioDocumentStorage(IConfiguration cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var endpoint = cfg["Docs:Storage:Minio:Endpoint"]
            ?? throw new InvalidOperationException("Docs:Storage:Minio:Endpoint not configured.");
        var accessKey = cfg["Docs:Storage:Minio:AccessKey"] ?? string.Empty;
        var secretKey = cfg["Docs:Storage:Minio:SecretKey"] ?? string.Empty;
        var secure = string.Equals(cfg["Docs:Storage:Minio:Secure"], "true", StringComparison.OrdinalIgnoreCase);
        _bucket = cfg["Docs:Storage:Minio:Bucket"] ?? "clawbot-docs";

        _client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(secure)
            .Build();
    }

    public async Task<string> SaveAsync(byte[] content, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName required", nameof(fileName));

        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucket), ct).ConfigureAwait(false);
        if (!exists)
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct).ConfigureAwait(false);

        using var ms = new MemoryStream(content);
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(fileName)
            .WithStreamData(ms)
            .WithObjectSize(ms.Length)
            .WithContentType("application/pdf"), ct).ConfigureAwait(false);

        return await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(fileName)
            .WithExpiry(ExpirySeconds)).ConfigureAwait(false);
    }
}
