using Clawbot.Agents.Core.Docs;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace Clawbot.Infrastructure.Documents;

// MinIO-backed document storage (M17): uploads objects and returns PublicBaseUrl when configured,
// otherwise a 7-day presigned GET URL.
public sealed class MinioDocumentStorage : IDocumentStorage
{
    private const int ExpirySeconds = 7 * 24 * 60 * 60; // 7 days

    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly string _publicBaseUrl;
    private volatile bool _bucketEnsured;

    public MinioDocumentStorage(IOptions<MinioOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;
        var endpoint = opts.Endpoint
            ?? throw new InvalidOperationException("Docs:Storage:Minio:Endpoint not configured.");
        _bucket = opts.Bucket;
        _publicBaseUrl = opts.PublicBaseUrl.TrimEnd('/');

        _client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(opts.AccessKey, opts.SecretKey)
            .WithSSL(opts.Secure)
            .Build();
    }

    public async Task<string> SaveAsync(byte[] content, string fileName, string? contentType = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName required", nameof(fileName));

        if (!_bucketEnsured)
        {
            var exists = await _client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(_bucket), ct).ConfigureAwait(false);
            if (!exists)
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct).ConfigureAwait(false);
            _bucketEnsured = true; // singleton; worst-case a couple of redundant checks under first-call races
        }

        using var ms = new MemoryStream(content);
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(fileName)
            .WithStreamData(ms)
            .WithObjectSize(ms.Length)
            .WithContentType(string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
            return $"{_publicBaseUrl}/{Uri.EscapeDataString(fileName)}";

        return await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(fileName)
            .WithExpiry(ExpirySeconds)).ConfigureAwait(false);
    }

    public async Task<byte[]> ReadAsync(string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName required", nameof(fileName));

        using var ms = new MemoryStream();
        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(fileName)
            .WithCallbackStream(stream => stream.CopyTo(ms)), ct).ConfigureAwait(false);

        return ms.ToArray();
    }
}
