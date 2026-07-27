using System.Buffers;
using System.Net.Http.Headers;
using System.Text;

namespace Clawbot.Infrastructure.Content.Publishing;

internal sealed class ZaloResponseBufferingHandler : DelegatingHandler
{
    private const int CopyBufferSize = 8192;

    private readonly long _maximumResponseBytes;

    public ZaloResponseBufferingHandler()
        : this(GraphSocialPublisher.ZaloMaxResponseContentBufferSize)
    {
    }

    internal ZaloResponseBufferingHandler(long maximumResponseBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResponseBytes);
        _maximumResponseBytes = maximumResponseBytes;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var headerBytes = CountResponseHeaderBytes(response);
            if (headerBytes > _maximumResponseBytes)
                throw new ZaloResponseSizeLimitExceededException();

            var content = response.Content;
            if (content is null)
                return response;

            var remainingBodyBytes = _maximumResponseBytes - headerBytes;
            if (content.Headers.ContentLength is > 0
                && content.Headers.ContentLength.Value > remainingBodyBytes)
            {
                throw new ZaloResponseSizeLimitExceededException();
            }

            var contentHeaders = content.Headers
                .Select(header => new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray()))
                .ToArray();
            var bufferedBody = await ReadBoundedAsync(
                content,
                remainingBodyBytes,
                cancellationToken).ConfigureAwait(false);
            var bufferedContent = new ByteArrayContent(bufferedBody);
            foreach (var header in contentHeaders)
            {
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    bufferedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = bufferedContent;
            content.Dispose();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static long CountResponseHeaderBytes(HttpResponseMessage response)
    {
        var statusLineBytes = Encoding.UTF8.GetByteCount(response.ReasonPhrase ?? string.Empty) + 32L;
        return checked(
            statusLineBytes
            + CountHeaderBytes(response.Headers)
            + CountHeaderBytes(response.Content?.Headers)
            + 2L);
    }

    private static long CountHeaderBytes(HttpHeaders? headers)
    {
        if (headers is null)
            return 0;

        long total = 0;
        foreach (var header in headers)
        {
            foreach (var value in header.Value)
            {
                total = checked(
                    total
                    + Encoding.UTF8.GetByteCount(header.Key)
                    + 2L
                    + Encoding.UTF8.GetByteCount(value)
                    + 2L);
            }
        }

        return total;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes < 0)
            throw new ZaloResponseSizeLimitExceededException();

        await using var stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream(
            capacity: (int)Math.Min(maximumBytes, CopyBufferSize));
        var rented = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long totalRead = 0;
            while (true)
            {
                var read = await stream
                    .ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                totalRead = checked(totalRead + read);
                if (totalRead > maximumBytes)
                    throw new ZaloResponseSizeLimitExceededException();

                await buffer
                    .WriteAsync(rented.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }

            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

internal sealed class ZaloResponseSizeLimitExceededException : HttpRequestException
{
    public ZaloResponseSizeLimitExceededException()
        : base("Zalo response exceeded the configured size limit.")
    {
    }
}
