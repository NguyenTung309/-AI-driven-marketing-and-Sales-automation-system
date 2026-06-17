using System.Collections.Concurrent;

namespace Clawbot.Infrastructure.Ads;

public interface IAdsPlatformThrottle
{
    Task<T> RunAsync<T>(
        string platform,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default);
}

public sealed class AdsPlatformThrottle : IAdsPlatformThrottle, IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public async Task<T> RunAsync<T>(
        string platform,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var gate = _gates.GetOrAdd(platform.Trim(), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await operation(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var gate in _gates.Values)
            gate.Dispose();
        _gates.Clear();
    }
}
